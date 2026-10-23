using System.Data;
using System.Text.Json;
using System.Security.Cryptography;
using Constia.Application.Bienvenida;
using Constia.Application.Correo;
using Constia.Domain;
using Microsoft.EntityFrameworkCore;
namespace Constia.Infrastructure.Bienvenida;

/// <summary>Transactions never escape this repository. SQL errors/uncertain commits are not replayed.</summary>
public sealed class OutboxBienvenidaRepository : IOutboxBienvenidaRepository
{
    private readonly ConstiaDbContext db;
    private readonly OpcionesProcesamientoBienvenida opciones;
    public OutboxBienvenidaRepository(ConstiaDbContext db, OpcionesProcesamientoBienvenida opciones)
    { this.db=db;this.opciones=opciones;db.Database.SetCommandTimeout(5); }
    private Task<DateTimeOffset> Ahora(CancellationToken ct)=>db.Database.SqlQueryRaw<DateTimeOffset>(
        "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00') AS [Value]").SingleAsync(ct);
    private async Task<ControlBienvenida> Control(CancellationToken ct)
    {
        if(!opciones.EsValida || !await new CompatibilidadEsquemaBienvenida(db).EsCompatibleAsync(ct))
            throw new InvalidOperationException("Esquema de bienvenida no disponible.");
        var c=await db.Set<ControlBienvenida>().FromSqlRaw(
            "SELECT * FROM dbo.ControlBienvenida WITH (UPDLOCK,HOLDLOCK) WHERE Id=1").AsNoTracking().SingleAsync(ct);
        if(c.VersionEsquema!=1 || c.ProtocoloEscritor!=1)throw new InvalidOperationException("Protocolo no disponible.");
        return c;
    }
    private bool Envia(ControlBienvenida c)=>opciones.ProcesamientoHabilitado && opciones.EnvioHabilitado &&
        opciones.Perfil is not null && c.EnvioHabilitado && !c.PausaProveedor;
    private Task<EventoBienvenida?> Propio(LeaseBienvenida l,CancellationToken ct)=>db.Set<EventoBienvenida>()
        .FromSqlInterpolated($"SELECT * FROM dbo.EventoBienvenida WITH (UPDLOCK,HOLDLOCK) WHERE Id={l.EventoId} AND LeaseId={l.Propietario} AND LeaseGeneracion={l.Generacion}")
        .AsNoTracking().SingleOrDefaultAsync(ct);
    private bool PayloadValido(EventoBienvenida e)
    {
        if(e.PayloadUtf8 is not {Length:>0 and <=16384} || e.PayloadSha256 is not {Length:32} ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(e.PayloadUtf8),e.PayloadSha256))return false;
        try
        {
            using var doc=JsonDocument.Parse(e.PayloadUtf8,new JsonDocumentOptions{MaxDepth=8});
            var j=doc.RootElement;
            return j.ValueKind==JsonValueKind.Object &&
                j.EnumerateObject().Select(p=>p.Name).SequenceEqual(new[]{"from","to","subject","html","text"}) &&
                j.GetProperty("from").ValueKind==JsonValueKind.String &&
                j.GetProperty("from").GetString()==$"CONSTIA <{opciones.Perfil!.Remitente}>" &&
                j.GetProperty("to").ValueKind==JsonValueKind.Array && j.GetProperty("to").GetArrayLength()==1 &&
                j.GetProperty("to")[0].ValueKind==JsonValueKind.String && j.GetProperty("to")[0].GetString()==e.Destinatario &&
                new[]{"subject","html","text"}.All(k=>j.GetProperty(k).ValueKind==JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(j.GetProperty(k).GetString()));
        }
        catch(JsonException){return false;}
    }
    private static bool Margen(EventoBienvenida e,DateTimeOffset n)=>e.ExpiraEnUtc>n.AddSeconds(10) &&
        (!e.HorizonteHttpEnUtc.HasValue || e.HorizonteHttpEnUtc>n.AddSeconds(10));
    private async Task<DateTimeOffset?> Capacidad(DateTimeOffset n,CancellationToken ct)
    {
        // All reservations (including future timestamps after a clock regression) count conservatively.
        var fechas=await db.Set<IntentoBienvenida>().Where(i=>i.ReservadoEnUtc>n.AddHours(-24))
            .OrderBy(i=>i.ReservadoEnUtc).Select(i=>i.ReservadoEnUtc).ToArrayAsync(ct);
        return fechas.Length<20?null:fechas[fechas.Length-20].AddHours(24);
    }
    public async Task<TrabajoBienvenida?> AdquirirAsync(CancellationToken ct)
    {
        if(!opciones.EsValida || !opciones.ProcesamientoHabilitado || !opciones.EnvioHabilitado)return null;
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var c=await Control(ct);if(!Envia(c))return null;
        var n=await Ahora(ct);var perfil=opciones.Perfil!.Huella;
        var e=await db.Set<EventoBienvenida>().FromSqlInterpolated($"""
            SELECT TOP (1) * FROM dbo.EventoBienvenida WITH (UPDLOCK,HOLDLOCK)
            WHERE Estado IN (1,4,5) AND LeaseId IS NULL AND Reservas<3 AND ExpiraEnUtc>{n.AddSeconds(10)}
              AND (HorizonteHttpEnUtc IS NULL OR HorizonteHttpEnUtc>{n.AddSeconds(10)})
              AND (ProximaEjecucionEnUtc IS NULL OR ProximaEjecucionEnUtc<={n})
              AND EpocaCaptura={c.EpocaCaptura}
              AND (Estado<>5 OR PayloadUtf8 IS NULL OR PerfilProveedorVersion={perfil})
            ORDER BY ProximaEjecucionEnUtc,CapturadoEnUtc,Id
            """).AsNoTracking().FirstOrDefaultAsync(ct);
        if(e is null)return null;
        var owner=Guid.NewGuid();var gen=checked(e.LeaseGeneracion+1);var hasta=n.AddSeconds(90);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET Estado=2,LeaseId={owner},LeaseGeneracion={gen},LeaseHastaUtc={hasta},
              UltimoLeaseHastaUtc=CASE WHEN UltimoLeaseHastaUtc>{hasta} THEN UltimoLeaseHastaUtc ELSE {hasta} END
            WHERE Id={e.Id}
            """,ct);
        await tx.CommitAsync(ct);return new(new(e.Id,owner,gen),e);
    }
    public async Task<bool> CongelarAsync(LeaseBienvenida l,byte[] payload,string perfil,CancellationToken ct)
    {
        var bytes=(byte[])payload.Clone();
        if(bytes.Length is <1 or >16384 || perfil!=opciones.Perfil?.Huella)throw new ArgumentException("Payload o perfil inválido.");
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var c=await Control(ct);var e=await Propio(l,ct);var n=await Ahora(ct);
        if(e is null || e.Estado!=EstadoEventoBienvenida.Tomado || e.LeaseHastaUtc<=n || !Envia(c))return false;
        var hash=SHA256.HashData(bytes);
        if(e.PayloadUtf8 is not null)
            return PayloadValido(e) && e.PerfilProveedorVersion==perfil && e.PayloadUtf8.SequenceEqual(bytes);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET PayloadUtf8={bytes},PayloadSha256={hash},PerfilProveedorVersion={perfil}
            WHERE Id={e.Id}
            """,ct);
        await tx.CommitAsync(ct);return true;
    }
    public async Task<ReservaBienvenida?> ReservarAsync(LeaseBienvenida l,Guid intentoId,CancellationToken ct)
    {
        if(intentoId==Guid.Empty)throw new ArgumentException("Identidad inválida.");
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var c=await Control(ct);var e=await Propio(l,ct);var n=await Ahora(ct);
        if(e is null || e.Estado!=EstadoEventoBienvenida.Tomado || e.LeaseHastaUtc<=n.AddSeconds(10) ||
            !Envia(c) || c.EpocaCaptura!=e.EpocaCaptura)return null;
        if(!PayloadValido(e) || e.PerfilProveedorVersion!=opciones.Perfil!.Huella)
        {await Reprogramar(e,n,true,ct);await tx.CommitAsync(ct);return null;}
        if(e.Reservas>=3 || !Margen(e,n))
        {await Finalizar(e,n,e.HuboIncertidumbre,e.Reservas>=3?MotivoFinalBienvenida.Agotado:MotivoFinalBienvenida.MargenInsuficiente,ct);await tx.CommitAsync(ct);return null;}
        var cupo=await Capacidad(n,ct);
        if(cupo.HasValue)
        {
            if(cupo>=e.ExpiraEnUtc || (e.HorizonteHttpEnUtc.HasValue && cupo>=e.HorizonteHttpEnUtc))
                await Finalizar(e,n,e.HuboIncertidumbre,MotivoFinalBienvenida.MargenInsuficiente,ct);
            else await Reprogramar(e,cupo.Value,false,ct);
            await tx.CommitAsync(ct);return null;
        }
        var ordinal=e.Reservas+1;var primera=e.PrimeraReservaEnUtc??n;
        var horizonte=primera.AddHours(23)<e.ExpiraEnUtc?primera.AddHours(23):e.ExpiraEnUtc;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET Estado=3,Reservas={ordinal},PrimeraReservaEnUtc={primera},HorizonteHttpEnUtc={horizonte}
            WHERE Id={e.Id};
            INSERT dbo.IntentoBienvenida(Id,EventoId,Ordinal,ReservadoEnUtc,PropietarioLeaseId,LeaseGeneracion,Estado)
            VALUES({intentoId},{e.Id},{ordinal},{n},{l.Propietario},{l.Generacion},1);
            """,ct);
        await tx.CommitAsync(ct);return new(l,intentoId,ordinal);
    }
    public async Task<PermisoHttpBienvenida?> AutorizarHttpAsync(ReservaBienvenida r,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var c=await Control(ct);var e=await Propio(r.Lease,ct);var n=await Ahora(ct);
        if(e is null || e.Estado!=EstadoEventoBienvenida.ReservadoSinResultado || e.Reservas!=r.Ordinal ||
            !Envia(c) || c.EpocaCaptura!=e.EpocaCaptura || !Margen(e,n) || e.LeaseHastaUtc<=n.AddSeconds(10) ||
            !PayloadValido(e) || e.PerfilProveedorVersion!=opciones.Perfil!.Huella)return null;
        var exists=await db.Set<IntentoBienvenida>().AnyAsync(i=>i.Id==r.IntentoId && i.EventoId==e.Id &&
            i.Estado==EstadoIntentoBienvenida.ReservadoSinResultado && i.PropietarioLeaseId==r.Lease.Propietario &&
            i.LeaseGeneracion==r.Lease.Generacion && i.Ordinal==r.Ordinal,ct);
        if(!exists)return null;
        var fin=e.LeaseHastaUtc!.Value<e.HorizonteHttpEnUtc!.Value?e.LeaseHastaUtc.Value:e.HorizonteHttpEnUtc.Value;
        var permiso=new PermisoHttpBienvenida(new(e.ClaveIdempotencia,e.PayloadUtf8!),fin-n);
        await tx.CommitAsync(ct);return permiso;
    }
    private Task<int> Cerrar(ReservaBienvenida r,int estado,bool? iniciado,int? motivo,int? status,DateTimeOffset n,CancellationToken ct)=>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.IntentoBienvenida SET Estado={estado},HttpIniciadoConfirmado={iniciado},
                MotivoEntrega={motivo},HttpStatus={status},FinalizadoEnUtc={n}
            WHERE Id={r.IntentoId} AND EventoId={r.Lease.EventoId} AND Estado=1
                AND PropietarioLeaseId={r.Lease.Propietario} AND LeaseGeneracion={r.Lease.Generacion}
            """,ct);
    private Task<int> Finalizar(EventoBienvenida e,DateTimeOffset n,bool incierto,MotivoFinalBienvenida motivo,CancellationToken ct)=>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET Estado={(byte)PoliticaProcesamientoBienvenida.Final(incierto)},
                HuboIncertidumbre={incierto},MotivoFinal={(byte)motivo},FinalizadoEnUtc={n},
                ProximaEjecucionEnUtc=NULL,LeaseId=NULL,LeaseHastaUtc=NULL WHERE Id={e.Id}
            """,ct);
    private Task<int> Reprogramar(EventoBienvenida e,DateTimeOffset siguiente,bool pausa,CancellationToken ct)=>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET Estado={(pausa?5:e.Reservas==0?1:4)},
                ProximaEjecucionEnUtc={(pausa?(DateTimeOffset?)null:siguiente)},LeaseId=NULL,LeaseHastaUtc=NULL
            WHERE Id={e.Id}
            """,ct);
    public async Task<bool> RegistrarResultadoAsync(ReservaBienvenida r,ResultadoEntregaCorreo resultado,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await Control(ct);
        var i=await db.Set<IntentoBienvenida>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==r.IntentoId,ct);
        if(i is null || i.EventoId!=r.Lease.EventoId || i.PropietarioLeaseId!=r.Lease.Propietario ||
            i.LeaseGeneracion!=r.Lease.Generacion)return false;
        var estado=resultado.Estado switch{
            EstadoEntregaCorreo.AceptadaPorProveedor=>2,EstadoEntregaCorreo.RechazadaDefinitivamente=>3,
            EstadoEntregaCorreo.FalloTemporal=>4,EstadoEntregaCorreo.CanceladaAntesDelEnvio=>5,
            EstadoEntregaCorreo.ProveedorNoDisponible=>resultado.IntentoHttpIniciado?3:5,_=>6};
        if(i.Estado!=EstadoIntentoBienvenida.ReservadoSinResultado)
            return (byte)i.Estado==estado && i.MotivoEntrega==(int)resultado.Motivo && i.HttpStatus==resultado.HttpStatus;
        var e=await Propio(r.Lease,ct);
        if(e is null || e.Estado!=EstadoEventoBienvenida.ReservadoSinResultado || e.Reservas!=r.Ordinal)return false;
        var n=await Ahora(ct);
        if(await Cerrar(r,estado,resultado.IntentoHttpIniciado,(int)resultado.Motivo,resultado.HttpStatus,n,ct)!=1)return false;
        var incierto=e.HuboIncertidumbre || estado==6;
        if(PoliticaProcesamientoBienvenida.EsPausa(resultado))
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.ControlBienvenida SET PausaProveedor=1,MotivoPausa={PoliticaProcesamientoBienvenida.MotivoPausa(resultado)} WHERE Id=1
                """,ct);
        if(estado==2)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.EventoBienvenida SET Estado=6,HuboIncertidumbre={incierto},MotivoFinal=0,
                    IdProveedor={resultado.IdProveedor},FinalizadoEnUtc={n},ProximaEjecucionEnUtc=NULL,LeaseId=NULL,LeaseHastaUtc=NULL WHERE Id={e.Id}
                """,ct);
        else
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.EventoBienvenida SET HuboIncertidumbre={incierto} WHERE Id={e.Id}",ct);
            if(e.Reservas>=3 || !Margen(e,n))
                await Finalizar(e,n,incierto,e.Reservas>=3?MotivoFinalBienvenida.Agotado:MotivoFinalBienvenida.Vencido,ct);
            else if(resultado.Estado==EstadoEntregaCorreo.ProveedorNoDisponible)
                await Reprogramar(e,n,true,ct);
            else if(estado==3 || !PoliticaReintentoCorreo.EsCandidatoMismaSolicitud(resultado) && estado!=5)
                await Finalizar(e,n,incierto,MotivoFinalBienvenida.Rechazo,ct);
            else
            {
                var next=n+PoliticaProcesamientoBienvenida.Espera(e.Reservas,resultado.EsperaMinima);
                if(next.AddSeconds(10)>=e.HorizonteHttpEnUtc)
                    await Finalizar(e,n,incierto,MotivoFinalBienvenida.MargenInsuficiente,ct);
                else await Reprogramar(e,next,false,ct);
            }
        }
        await tx.CommitAsync(ct);return true;
    }
    public async Task LiberarAsync(LeaseBienvenida l,bool pausar,MotivoFinalBienvenida? final,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await Control(ct);var e=await Propio(l,ct);if(e is null)return;
        var n=await Ahora(ct);
        if(e.LeaseHastaUtc<=n)return;
        // This API is only used before the caller invokes transport.
        if(e.Estado==EstadoEventoBienvenida.ReservadoSinResultado)
        {
            var i=await db.Set<IntentoBienvenida>().AsNoTracking().SingleAsync(x=>x.EventoId==e.Id && x.Ordinal==e.Reservas,ct);
            if(i.Estado!=EstadoIntentoBienvenida.ReservadoSinResultado)return;
            await Cerrar(new(l,i.Id,e.Reservas),5,false,(int)MotivoEntregaCorreo.Cancelacion,null,n,ct);
        }
        if(final.HasValue || e.Reservas>=3 || !Margen(e,n))
            await Finalizar(e,n,e.HuboIncertidumbre,final??(e.Reservas>=3?MotivoFinalBienvenida.Agotado:MotivoFinalBienvenida.Vencido),ct);
        else await Reprogramar(e,n+PoliticaProcesamientoBienvenida.Espera(e.Reservas,null),pausar,ct);
        await tx.CommitAsync(ct);
    }
    public async Task PausarProveedorAsync(ResultadoEntregaCorreo r,CancellationToken ct)
    {
        if(!PoliticaProcesamientoBienvenida.EsPausa(r))throw new ArgumentException("No representa una pausa.");
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await Control(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.ControlBienvenida SET PausaProveedor=1,MotivoPausa={PoliticaProcesamientoBienvenida.MotivoPausa(r)} WHERE Id=1",ct);
        await tx.CommitAsync(ct);
    }
    public async Task<int> RecuperarYVencerAsync(int maximo,CancellationToken ct)
    {
        if(maximo is <1 or >100)throw new ArgumentOutOfRangeException(nameof(maximo));
        if(!opciones.EsValida || !opciones.MantenimientoHabilitado)return 0;
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await Control(ct);var n=await Ahora(ct);
        var items=await db.Set<EventoBienvenida>().FromSqlInterpolated($"""
            SELECT TOP ({maximo}) * FROM dbo.EventoBienvenida WITH (UPDLOCK,HOLDLOCK)
            WHERE Estado BETWEEN 1 AND 5 AND
              ((Estado IN (2,3) AND LeaseHastaUtc<={n}) OR
               (LeaseId IS NULL AND (ExpiraEnUtc<={n.AddSeconds(10)} OR Reservas>=3 OR HorizonteHttpEnUtc<={n.AddSeconds(10)})))
            ORDER BY CapturadoEnUtc,Id
            """).AsNoTracking().ToArrayAsync(ct);
        foreach(var e in items)
        {
            var incierto=e.HuboIncertidumbre;var siguiente=n;
            var abiertos=await db.Set<IntentoBienvenida>().AsNoTracking()
                .Where(i=>i.EventoId==e.Id && i.Estado==EstadoIntentoBienvenida.ReservadoSinResultado).ToArrayAsync(ct);
            if(abiertos.Length>1)throw new InvalidOperationException("Intentos incoherentes.");
            foreach(var i in abiertos)
            {
                if(e.LeaseId!=i.PropietarioLeaseId || e.LeaseGeneracion!=i.LeaseGeneracion || e.Reservas!=i.Ordinal)
                    throw new InvalidOperationException("Propiedad incoherente.");
                await Cerrar(new(new(e.Id,i.PropietarioLeaseId,i.LeaseGeneracion),i.Id,i.Ordinal),6,null,(int)MotivoEntregaCorreo.Red,null,n,ct);
                incierto=true;siguiente=i.ReservadoEnUtc+PoliticaProcesamientoBienvenida.Espera(e.Reservas,null);
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.EventoBienvenida SET HuboIncertidumbre={incierto} WHERE Id={e.Id}",ct);
            if(e.Reservas>=3 || !Margen(e,n))
                await Finalizar(e,n,incierto,e.Reservas>=3?MotivoFinalBienvenida.Agotado:MotivoFinalBienvenida.Vencido,ct);
            else
            {
                var cupo=await Capacidad(n,ct);
                if(cupo.HasValue && cupo>siguiente)siguiente=cupo.Value;
                if(siguiente<n)siguiente=n;
                if(siguiente.AddSeconds(10)>=e.ExpiraEnUtc || siguiente.AddSeconds(10)>=e.HorizonteHttpEnUtc)
                    await Finalizar(e,n,incierto,MotivoFinalBienvenida.MargenInsuficiente,ct);
                else await Reprogramar(e,siguiente,false,ct);
            }
        }
        await tx.CommitAsync(ct);return items.Length;
    }
}
