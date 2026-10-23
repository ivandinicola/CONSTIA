using System.Data.Common;
using Constia.Application.Bienvenida;
using Constia.Application.Correo;
using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Bienvenida;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Constia.API.Tests;

// Opt-in SQL only: every test uses an independently created database in the verified CI service.
public sealed class BienvenidaProcesamientoSqlServerTests
{
    private static readonly Guid Epoca=Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static OpcionesProcesamientoBienvenida Opciones=>OpcionesProcesamientoBienvenida.DesdeValores(
        "true","true","true","cuenta-test","correo@example.invalid");
    private static ResultadoEntregaCorreo Aceptada=>new(EstadoEntregaCorreo.AceptadaPorProveedor,MotivoEntregaCorreo.Ninguno,"opaque-id",200,intentoHttpIniciado:true);
    private static ResultadoEntregaCorreo Temporal=>new(EstadoEntregaCorreo.FalloTemporal,MotivoEntregaCorreo.LimiteVelocidad,
        httpStatus:429,esperaMinima:TimeSpan.FromMinutes(2),intentoHttpIniciado:true);
    private sealed class F : IAsyncDisposable
    {
        public EmailVerificationSqlDatabase Database=null!;
        public Guid Id,UsuarioId;
        public ConstiaDbContext Db(params IInterceptor[] i)=>Database.CreateContext(i);
        public OutboxBienvenidaRepository Repo(ConstiaDbContext d,OpcionesProcesamientoBienvenida? o=null)=>new(d,o??Opciones);
        public static async Task<F> Crear(string? email=null)
        {
            var f=new F{Database=await EmailVerificationSqlDatabase.CreateAsync()};
            await using var d=f.Db();await d.Database.MigrateAsync();
            var now=DateTimeOffset.UtcNow;
            var u=new Usuario("SQL C3B",email??$"{Guid.NewGuid():N}@example.invalid","hash-sintetico","Etc/UTC");
            // The historical trigger requires the first durable state to be Pending.
            d.Add(u);await d.SaveChangesAsync();
            await using(var insertado=f.Db())
            {
                var pendiente=await insertado.Set<Usuario>().AsNoTracking().SingleAsync(x=>x.Id==u.Id);
                Assert.Equal(EstadoVerificacionCorreo.Pendiente,pendiente.EstadoVerificacionCorreo);
                Assert.Null(pendiente.EmailVerificadoEnUtc);
            }

            EventoBienvenida e;
            await using(var tx=await d.Database.BeginTransactionAsync())
            {
                u.ConfirmarCorreo(now);
                e=EventoBienvenida.CrearPendiente(u.Id,Epoca,u.Email,now,now);
                d.Add(e);await d.SaveChangesAsync();
                await tx.CommitAsync();
            }
            f.Id=e.Id;f.UsuarioId=u.Id;
            await d.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.ControlBienvenida SET CapturaHabilitada=1,EpocaCaptura={Epoca},
                  InicioCapturaEnUtc=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00'),EnvioHabilitado=1 WHERE Id=1
                """);
            await using(var lectura=f.Db())
            {
                var usuario=await lectura.Set<Usuario>().AsNoTracking().SingleAsync(x=>x.Id==f.UsuarioId);
                var evento=await lectura.Set<EventoBienvenida>().AsNoTracking().SingleAsync(x=>x.Id==f.Id);
                Assert.Equal(EstadoVerificacionCorreo.Verificado,usuario.EstadoVerificacionCorreo);
                Assert.Equal(now,usuario.EmailVerificadoEnUtc);Assert.Equal(u.Email,usuario.Email);
                Assert.Equal(EstadoEventoBienvenida.Pendiente,evento.Estado);
                Assert.Equal(usuario.Id,evento.UsuarioId);Assert.Equal(usuario.Email,evento.Destinatario);
                Assert.Equal(Epoca,evento.EpocaCaptura);Assert.Equal(now,evento.ConfirmadoEnUtc);
                Assert.Equal(now,evento.CapturadoEnUtc);Assert.Equal(now.AddHours(24),evento.ExpiraEnUtc);
                Assert.Equal(now,evento.ProximaEjecucionEnUtc);Assert.Equal(0,evento.Reservas);
                Assert.Null(evento.LeaseId);Assert.Null(evento.PayloadUtf8);
                Assert.Empty(await lectura.Set<IntentoBienvenida>().AsNoTracking().ToArrayAsync());
                var control=await lectura.Set<ControlBienvenida>().AsNoTracking().SingleAsync();
                Assert.Equal(Epoca,control.EpocaCaptura);
            }
            return f;
        }
        public async Task<EventoBienvenida> Evento()
        {await using var d=Db();return await d.Set<EventoBienvenida>().AsNoTracking().SingleAsync(e=>e.Id==Id);}
        public async Task<TrabajoBienvenida> Tomar()
        {await using var d=Db();return Assert.IsType<TrabajoBienvenida>(await Repo(d).AdquirirAsync(default));}
        public async Task<ReservaBienvenida> Reservar(TrabajoBienvenida? t=null)
        {
            t??=await Tomar();await using var d=Db();var r=Repo(d);
            if(t.Evento.PayloadUtf8 is null)Assert.True(await r.CongelarAsync(t.Lease,
                new PreparadorPayloadBienvenida(Opciones.Perfil!).Preparar(t.Evento),Opciones.Perfil!.Huella,default));
            return Assert.IsType<ReservaBienvenida>(await r.ReservarAsync(t.Lease,Guid.NewGuid(),default));
        }
        public async Task CaducarLease()
        {await using var d=Db();await d.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.EventoBienvenida SET LeaseHastaUtc=DATEADD(second,-1,TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00')) WHERE Id={Id} AND LeaseId IS NOT NULL");}
        public async Task HacerElegible()
        {await using var d=Db();await d.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.EventoBienvenida SET ProximaEjecucionEnUtc=DATEADD(second,-1,TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00')) WHERE Id={Id} AND LeaseId IS NULL");}
        public async Task SembrarReservas(int count,int antiguedadHoras=1)
        {
            await using var d=Db();var now=DateTimeOffset.UtcNow;var reserved=now.AddHours(-antiguedadHoras);
            for(var k=0;k<count;k++)
            {
                var u=new Usuario("Fixture",$"{Guid.NewGuid():N}@example.invalid","hash","Etc/UTC");
                d.Add(u);await d.SaveChangesAsync();
                await using var tx=await d.Database.BeginTransactionAsync();
                u.ConfirmarCorreo(reserved.AddHours(-1));
                var e=EventoBienvenida.CrearPendiente(u.Id,Epoca,u.Email,reserved.AddHours(-1),reserved.AddHours(-1));
                d.Add(e);await d.SaveChangesAsync();
                var payload=new PreparadorPayloadBienvenida(Opciones.Perfil!).Preparar(e);
                var hash=System.Security.Cryptography.SHA256.HashData(payload);
                var id=Guid.NewGuid();var owner=Guid.NewGuid();
                await d.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE dbo.EventoBienvenida SET Estado=7,MotivoFinal=1,Reservas=1,PrimeraReservaEnUtc={reserved},
                      HorizonteHttpEnUtc=DATEADD(hour,23,{reserved}),FinalizadoEnUtc={reserved.AddMinutes(1)},
                      ProximaEjecucionEnUtc=NULL,PayloadUtf8={payload},PayloadSha256={hash},PerfilProveedorVersion={Opciones.Perfil!.Huella},
                      LeaseGeneracion=1,UltimoLeaseHastaUtc={reserved.AddSeconds(90)}
                    WHERE Id={e.Id};
                    INSERT dbo.IntentoBienvenida(Id,EventoId,Ordinal,ReservadoEnUtc,PropietarioLeaseId,LeaseGeneracion,
                      Estado,HttpIniciadoConfirmado,MotivoEntrega,HttpStatus,FinalizadoEnUtc)
                    VALUES({id},{e.Id},1,{reserved},{owner},1,3,1,4,422,{reserved.AddMinutes(1)});
                    """);
                await tx.CommitAsync();
            }
        }
        public ValueTask DisposeAsync()=>Database.DisposeAsync();
    }
    private static Task<T[]> Competir<T>(Func<Task<T>> a,Func<Task<T>> b)
    {
        var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var ready=0;
        async Task<T> Run(Func<Task<T>> f){if(Interlocked.Increment(ref ready)==2)start.SetResult();await start.Task;return await f();}
        return Task.WhenAll(Run(a),Run(b));
    }
    [C3BSqlFact] public async Task S01_AdquisicionExclusiva()
    {
        await using var f=await F.Crear();await using var a=f.Db();await using var b=f.Db();
        var rows=await Competir(()=>f.Repo(a).AdquirirAsync(default),()=>f.Repo(b).AdquirirAsync(default));
        Assert.Single(rows,x=>x is not null);Assert.Equal(EstadoEventoBienvenida.Tomado,(await f.Evento()).Estado);
    }
    [C3BSqlFact] public async Task S02_LeaseVencidoNuevaGeneracion()
    {
        await using var f=await F.Crear();var t=await f.Tomar();await f.CaducarLease();
        await using var d=f.Db();Assert.Equal(1,await f.Repo(d).RecuperarYVencerAsync(100,default));
        var nuevo=await f.Tomar();Assert.True(nuevo.Lease.Generacion>t.Lease.Generacion);Assert.NotEqual(t.Lease.Propietario,nuevo.Lease.Propietario);
    }
    [C3BSqlFact] public async Task S03_ReservaConcurrenteOrdinalUnico()
    {
        await using var f=await F.Crear();var t=await f.Tomar();
        await using(var d=f.Db())Assert.True(await f.Repo(d).CongelarAsync(t.Lease,new PreparadorPayloadBienvenida(Opciones.Perfil!).Preparar(t.Evento),Opciones.Perfil!.Huella,default));
        await using var a=f.Db();await using var b=f.Db();
        var rows=await Competir(()=>f.Repo(a).ReservarAsync(t.Lease,Guid.NewGuid(),default),()=>f.Repo(b).ReservarAsync(t.Lease,Guid.NewGuid(),default));
        Assert.Single(rows,x=>x is not null);Assert.Equal(1,await a.Set<IntentoBienvenida>().CountAsync());
    }
    [C3BSqlFact] public async Task S04_PresupuestoDiecinueveDosCompetidores()
    {
        await using var f=await F.Crear();await f.SembrarReservas(19);var t=await f.Tomar();
        await using var a=f.Db();await using var b=f.Db();
        var now=DateTimeOffset.UtcNow;var u=new Usuario("Concurrente",$"{Guid.NewGuid():N}@example.invalid","hash","Etc/UTC");
        a.Add(u);await a.SaveChangesAsync();
        await using(var tx=await a.Database.BeginTransactionAsync())
        {
            u.ConfirmarCorreo(now);a.Add(EventoBienvenida.CrearPendiente(u.Id,Epoca,u.Email,now,now));
            await a.SaveChangesAsync();await tx.CommitAsync();
        }
        var t2=Assert.IsType<TrabajoBienvenida>(await f.Repo(b).AdquirirAsync(default));
        await f.Repo(a).CongelarAsync(t.Lease,new PreparadorPayloadBienvenida(Opciones.Perfil!).Preparar(t.Evento),Opciones.Perfil!.Huella,default);
        await f.Repo(b).CongelarAsync(t2.Lease,new PreparadorPayloadBienvenida(Opciones.Perfil!).Preparar(t2.Evento),Opciones.Perfil!.Huella,default);
        var rows=await Competir(()=>f.Repo(a).ReservarAsync(t.Lease,Guid.NewGuid(),default),()=>f.Repo(b).ReservarAsync(t2.Lease,Guid.NewGuid(),default));
        Assert.Single(rows,x=>x is not null);Assert.Equal(20,await a.Set<IntentoBienvenida>().CountAsync());
        Assert.False((await a.Set<ControlBienvenida>().AsNoTracking().SingleAsync()).PausaProveedor);
    }
    [C3BSqlFact] public async Task S05_MaximoTresReservas()
    {
        await using var f=await F.Crear();
        for(var k=0;k<3;k++){var r=await f.Reservar();await using var d=f.Db();
            Assert.True(await f.Repo(d).RegistrarResultadoAsync(r,Temporal,default));await f.HacerElegible();}
        var e=await f.Evento();Assert.Equal(3,e.Reservas);Assert.Equal(EstadoEventoBienvenida.FinalSinAceptacion,e.Estado);
        await using var check=f.Db();Assert.Null(await f.Repo(check).AdquirirAsync(default));
    }
    [C3BSqlFact] public async Task S06_PropietarioAnteriorRechazado()
    {
        await using var f=await F.Crear();var old=await f.Reservar();await f.CaducarLease();
        await using var d=f.Db();
        await f.Repo(d).LiberarAsync(old.Lease,false,null,default);
        Assert.Equal(EstadoEventoBienvenida.ReservadoSinResultado,(await f.Evento()).Estado);
        await f.Repo(d).RecuperarYVencerAsync(100,default);await f.HacerElegible();var nuevo=await f.Tomar();
        Assert.True(nuevo.Lease.Generacion>old.Lease.Generacion);
        Assert.Null(await f.Repo(d).AutorizarHttpAsync(old,default));
        Assert.False(await f.Repo(d).RegistrarResultadoAsync(old,Aceptada,default));
        Assert.NotEqual(EstadoEventoBienvenida.AceptadoProveedor,(await f.Evento()).Estado);

        // A stale result must also fail after the new generation has its own durable result.
        var actual=await f.Reservar(nuevo);
        Assert.True(await f.Repo(d).RegistrarResultadoAsync(actual,Temporal,default));
        var antes=await f.Evento();
        Assert.False(await f.Repo(d).RegistrarResultadoAsync(old,Aceptada,default));
        var despues=await f.Evento();
        Assert.Equal(EstadoEventoBienvenida.EsperandoReintento,despues.Estado);
        Assert.Equal(antes.LeaseGeneracion,despues.LeaseGeneracion);
        Assert.Equal(antes.ProximaEjecucionEnUtc,despues.ProximaEjecucionEnUtc);
        Assert.Equal(antes.HuboIncertidumbre,despues.HuboIncertidumbre);
        Assert.Equal(antes.IdProveedor,despues.IdProveedor);
        MismaSolicitudDurable(antes,despues);
        var intentos=await d.Set<IntentoBienvenida>().AsNoTracking().OrderBy(i=>i.Ordinal).ToArrayAsync();
        Assert.Equal(2,intentos.Length);
        Assert.Equal(old.IntentoId,intentos[0].Id);Assert.Equal(EstadoIntentoBienvenida.Incierto,intentos[0].Estado);
        Assert.Equal(actual.IntentoId,intentos[1].Id);Assert.Equal(EstadoIntentoBienvenida.TemporalConocido,intentos[1].Estado);
        Assert.Equal<short?>(429,intentos[1].HttpStatus);
    }
    [C3BSqlFact] public async Task S07_PayloadPerfilYHashTrasReinicio()
    {
        await using var f=await F.Crear();var r=await f.Reservar();var antes=await f.Evento();
        await using(var d=f.Db())Assert.True(await f.Repo(d).RegistrarResultadoAsync(r,Temporal,default));
        await f.HacerElegible();
        var distinto=OpcionesProcesamientoBienvenida.DesdeValores("true","true","true","otra-cuenta","correo@example.invalid");
        var transporte=new TransporteProhibido();
        await using(var d=f.Db())await new ProcesarBienvenida(f.Repo(d,distinto),
            new PreparadorPayloadBienvenida(distinto.Perfil!),transporte,TimeProvider.System).EjecutarAsync(default);
        Assert.Equal(0,transporte.Calls);Assert.Equal(EstadoEventoBienvenida.Pausado,(await f.Evento()).Estado);
        Assert.Equal(antes.PayloadUtf8,(await f.Evento()).PayloadUtf8);
        var nuevo=await f.Reservar();
        await using var check=f.Db();var permiso=await f.Repo(check).AutorizarHttpAsync(nuevo,default);
        Assert.NotNull(permiso);Assert.Equal(antes.PayloadUtf8,permiso.Solicitud.CopiarPayload());
        var despues=await f.Evento();Assert.Equal(antes.PayloadSha256,despues.PayloadSha256);
        Assert.Equal(antes.ClaveIdempotencia,permiso.Solicitud.ClaveIdempotencia);Assert.Equal(antes.PerfilProveedorVersion,despues.PerfilProveedorVersion);
    }
    [C3BSqlFact] public async Task S08_CommitCongelamientoIncierto()
    {
        await using var f=await F.Crear();var t=await f.Tomar();await using var d=f.Db(new CommitPerdido());
        await Assert.ThrowsAsync<FalloSimulado>(()=>f.Repo(d).CongelarAsync(t.Lease,new PreparadorPayloadBienvenida(Opciones.Perfil!).Preparar(t.Evento),Opciones.Perfil!.Huella,default));
        var e=await f.Evento();Assert.Equal(new PreparadorPayloadBienvenida(Opciones.Perfil!).Preparar(t.Evento),e.PayloadUtf8);Assert.Equal(0,e.Reservas);
        await using var check=f.Db();Assert.Empty(await check.Set<IntentoBienvenida>().ToArrayAsync());
    }
    [C3BSqlFact] public async Task S09_CommitReservaIncierto()
    {
        await using var f=await F.Crear();var t=await f.Tomar();
        await using(var d=f.Db())await f.Repo(d).CongelarAsync(t.Lease,new PreparadorPayloadBienvenida(Opciones.Perfil!).Preparar(t.Evento),Opciones.Perfil!.Huella,default);
        await using(var d=f.Db(new CommitPerdido()))await Assert.ThrowsAsync<FalloSimulado>(()=>f.Repo(d).ReservarAsync(t.Lease,Guid.NewGuid(),default));
        await using var c=f.Db();var i=Assert.Single(await c.Set<IntentoBienvenida>().ToArrayAsync());
        Assert.Equal(EstadoIntentoBienvenida.ReservadoSinResultado,i.Estado);Assert.Null(i.HttpIniciadoConfirmado);
        Assert.Equal(1,(await f.Evento()).Reservas);
    }
    [C3BSqlFact] public async Task S10_CaidaAntesHttpNoInventaCerteza()
    {
        await using var f=await F.Crear();await f.Reservar();await f.CaducarLease();
        await using var d=f.Db();await f.Repo(d).RecuperarYVencerAsync(100,default);
        var i=await d.Set<IntentoBienvenida>().AsNoTracking().SingleAsync();
        Assert.Equal(EstadoIntentoBienvenida.Incierto,i.Estado);Assert.Null(i.HttpIniciadoConfirmado);
        Assert.True((await f.Evento()).HuboIncertidumbre);
    }
    [C3BSqlFact] public async Task S11_IncertidumbreDuranteHttp()
    {
        await using var f=await F.Crear();var r=await f.Reservar();await using var d=f.Db();
        Assert.True(await f.Repo(d).RegistrarResultadoAsync(r,new(EstadoEntregaCorreo.ResultadoIncierto,MotivoEntregaCorreo.Timeout,intentoHttpIniciado:true),default));
        var e=await f.Evento();Assert.True(e.HuboIncertidumbre);Assert.Equal(EstadoEventoBienvenida.EsperandoReintento,e.Estado);
        Assert.True((await d.Set<IntentoBienvenida>().AsNoTracking().SingleAsync()).HttpIniciadoConfirmado);
    }
    [C3BSqlFact] public async Task S12_AceptacionConRespuestaPerdida()
    {
        await using var f=await F.Crear();var r=await f.Reservar();await using var d=f.Db();
        Assert.NotNull(await f.Repo(d).AutorizarHttpAsync(r,default));
        // Simulates provider acceptance followed by process loss: no SQL result was recorded.
        await f.CaducarLease();await f.Repo(d).RecuperarYVencerAsync(100,default);
        Assert.True((await f.Evento()).HuboIncertidumbre);
        Assert.NotEqual(EstadoEventoBienvenida.AceptadoProveedor,(await f.Evento()).Estado);
    }
    [C3BSqlFact] public async Task S13_CommitResultadoInciertoReconciliable()
    {
        await using var f=await F.Crear();var r=await f.Reservar();await f.CaducarLease();
        // Recording a known late result is allowed only before a new generation recovers.
        await using(var d=f.Db(new CommitPerdido()))await Assert.ThrowsAsync<FalloSimulado>(()=>f.Repo(d).RegistrarResultadoAsync(r,Aceptada,default));
        await using var check=f.Db();Assert.True(await f.Repo(check).RegistrarResultadoAsync(r,Aceptada,default));
        Assert.Equal(EstadoEventoBienvenida.AceptadoProveedor,(await f.Evento()).Estado);
        Assert.Equal(1,await check.Set<IntentoBienvenida>().CountAsync());
    }
    [C3BSqlFact] public async Task S14_HorizonteYExpiracionNoSeExtienden()
    {
        await using var f=await F.Crear();var r=await f.Reservar();var e=await f.Evento();
        Assert.Equal(e.PrimeraReservaEnUtc!.Value.AddHours(23),e.HorizonteHttpEnUtc);
        await using var d=f.Db();
        var old=DateTimeOffset.UtcNow.AddHours(-25);
        await d.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET CapturadoEnUtc={old},ExpiraEnUtc={old.AddHours(24)},
                PrimeraReservaEnUtc={old},HorizonteHttpEnUtc={old.AddHours(23)} WHERE Id={f.Id}
            """);
        Assert.Null(await f.Repo(d).AutorizarHttpAsync(r,default));
    }
    [C3BSqlFact] public async Task S15_VentanaMovilNoConfundePresupuestoConPausa()
    {
        await using var f=await F.Crear();await f.SembrarReservas(20);
        var t=await f.Tomar();await using var d=f.Db();await f.Repo(d).CongelarAsync(t.Lease,new PreparadorPayloadBienvenida(Opciones.Perfil!).Preparar(t.Evento),Opciones.Perfil!.Huella,default);
        Assert.Null(await f.Repo(d).ReservarAsync(t.Lease,Guid.NewGuid(),default));
        var e=await f.Evento();Assert.Equal(0,e.Reservas);Assert.NotNull(e.ProximaEjecucionEnUtc);
        Assert.False((await d.Set<ControlBienvenida>().AsNoTracking().SingleAsync()).PausaProveedor);
        var past=DateTimeOffset.UtcNow.AddHours(-25);
        await d.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET CapturadoEnUtc={past.AddHours(-1)},ExpiraEnUtc={past.AddHours(23)},
              PrimeraReservaEnUtc={past},HorizonteHttpEnUtc={past.AddHours(23)},FinalizadoEnUtc={past.AddMinutes(1)}
              WHERE Id<>{f.Id};
            UPDATE dbo.IntentoBienvenida SET ReservadoEnUtc={past},FinalizadoEnUtc={past.AddMinutes(1)};
            """);
        await f.HacerElegible();Assert.NotNull(await f.Reservar());
    }
    [C3BSqlFact] public async Task S16_PausaProveedorDurable()
    {
        foreach(var caso in new[]{(MotivoEntregaCorreo.Autorizacion,401,1,true),
            (MotivoEntregaCorreo.CuotaDiaria,429,3,false),(MotivoEntregaCorreo.CuotaMensual,429,4,false)})
        {
            await using var f=await F.Crear();var r=await f.Reservar();await using var d=f.Db();
            if(caso.Item4)
            {
                await f.Repo(d).RegistrarResultadoAsync(r,new(EstadoEntregaCorreo.ResultadoIncierto,
                    MotivoEntregaCorreo.Timeout,intentoHttpIniciado:true),default);
                await f.HacerElegible();r=await f.Reservar();
            }
            var q=new ResultadoEntregaCorreo(EstadoEntregaCorreo.ProveedorNoDisponible,caso.Item1,
                httpStatus:caso.Item2,intentoHttpIniciado:true);
            await f.Repo(d).RegistrarResultadoAsync(r,q,default);
            var c=await d.Set<ControlBienvenida>().AsNoTracking().SingleAsync();
            Assert.True(c.PausaProveedor);Assert.Equal(caso.Item3,c.MotivoPausa);
            var e=await f.Evento();Assert.Equal(EstadoEventoBienvenida.Pausado,e.Estado);
            Assert.Equal(caso.Item4,e.HuboIncertidumbre);
            await using var other=f.Db();Assert.Null(await f.Repo(other).AdquirirAsync(default));
        }
    }
    [C3BSqlFact] public async Task S17_ApagadoImpideAutorizar()
    {
        await using var f=await F.Crear();var r=await f.Reservar();var barrier=new ControlEnCurso();
        await using var d=f.Db(barrier);await using var other=f.Db();
        var permiso=f.Repo(d).AutorizarHttpAsync(r,default);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // The competing shutdown must serialize behind the authorization transaction.
            var ex=await Assert.ThrowsAsync<SqlException>(()=>other.Database.ExecuteSqlRawAsync("""
                IF @@LOCK_TIMEOUT<>-1 THROW 50000,'Unexpected initial lock timeout',1;
                BEGIN TRY
                    SET LOCK_TIMEOUT 1000;
                    UPDATE dbo.ControlBienvenida SET EnvioHabilitado=0 WHERE Id=1;
                    SET LOCK_TIMEOUT -1;
                END TRY
                BEGIN CATCH
                    SET LOCK_TIMEOUT -1;
                    THROW;
                END CATCH
                """));
            Assert.Equal(1222,ex.Number);
        }
        finally {barrier.Release.TrySetResult();await permiso;}
        Assert.NotNull(await permiso);
        await other.Database.ExecuteSqlRawAsync("UPDATE dbo.ControlBienvenida SET EnvioHabilitado=0 WHERE Id=1");
        Assert.Null(await f.Repo(other).AutorizarHttpAsync(r,default));
        Assert.Equal(1,(await f.Evento()).Reservas);
    }
    [C3BSqlFact] public async Task S18_RecuperacionConcurrenteUnSoloCierre()
    {
        await using var f=await F.Crear();await f.Reservar();await f.CaducarLease();
        await using var a=f.Db();await using var b=f.Db();
        var n=await Competir(()=>f.Repo(a).RecuperarYVencerAsync(100,default),()=>f.Repo(b).RecuperarYVencerAsync(100,default));
        Assert.Equal(1,n.Sum());Assert.Equal(1,await a.Set<IntentoBienvenida>().CountAsync());
        Assert.Equal(EstadoIntentoBienvenida.Incierto,(await a.Set<IntentoBienvenida>().AsNoTracking().SingleAsync()).Estado);
    }
    [C3BSqlFact] public async Task S19_VencimientoConEnvioApagado()
    {
        await using var f=await F.Crear();var t=await f.Tomar();await using var d=f.Db();
        await f.Repo(d).CongelarAsync(t.Lease,new PreparadorPayloadBienvenida(Opciones.Perfil!).Preparar(t.Evento),Opciones.Perfil!.Huella,default);
        await f.Repo(d).LiberarAsync(t.Lease,true,null,default);
        var frozen=(await f.Evento()).PayloadUtf8;var old=DateTimeOffset.UtcNow.AddHours(-25);
        await d.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.EventoBienvenida SET CapturadoEnUtc={old},ExpiraEnUtc={old.AddHours(24)},Estado=5,ProximaEjecucionEnUtc=NULL WHERE Id={f.Id}");
        var off=OpcionesProcesamientoBienvenida.DesdeValores("false","false","true",null,null);
        Assert.Equal(1,await f.Repo(d,off).RecuperarYVencerAsync(100,default));
        Assert.Equal(EstadoEventoBienvenida.FinalSinAceptacion,(await f.Evento()).Estado);
        Assert.Equal(frozen,(await f.Evento()).PayloadUtf8);
        Assert.Equal(0,await d.Set<IntentoBienvenida>().CountAsync());
    }
    [C3BSqlFact] public async Task S20_DestinatarioHistoricoNoAfectaCuenta()
    {
        await using var f=await F.Crear(new string('a',260)+"@example.invalid");await using var d=f.Db();
        var t=await f.Tomar();
        Assert.Throws<ArgumentException>(()=>new PreparadorPayloadBienvenida(Opciones.Perfil!).Preparar(t.Evento));
        await f.Repo(d).LiberarAsync(t.Lease,false,MotivoFinalBienvenida.DestinatarioNoElegible,default);
        Assert.Equal(EstadoVerificacionCorreo.Verificado,(await d.Set<Usuario>().SingleAsync()).EstadoVerificacionCorreo);
        Assert.Equal(0,(await f.Evento()).Reservas);
    }
    [C3BSqlFact] public async Task S21_MantenimientoRespetaLeaseActivo()
    {
        await using var f=await F.Crear();var r=await f.Reservar();await using var a=f.Db();await using var b=f.Db();
        var resultados=await Competir(()=>f.Repo(a).RegistrarResultadoAsync(r,Temporal,default),async()=>{
            var changed=await f.Repo(b).RecuperarYVencerAsync(100,default);
            changed+=await new RetencionBienvenidaRepository(b).AplicarAsync(100,default);
            return changed>0;
        });
        Assert.True(resultados[0]);Assert.False(resultados[1]);
        var e=await f.Evento();Assert.NotNull(e.PayloadUtf8);
        Assert.Equal(EstadoEventoBienvenida.EsperandoReintento,e.Estado);
        Assert.False(e.HuboIncertidumbre);
        Assert.Equal(EstadoIntentoBienvenida.TemporalConocido,(await a.Set<IntentoBienvenida>().AsNoTracking().SingleAsync()).Estado);
    }
    [C3BSqlFact] public async Task S22_RetencionConservaDeduplicacion()
    {
        await using var f=await F.Crear();var r=await f.Reservar();await using var d=f.Db();
        await f.Repo(d).RegistrarResultadoAsync(r,Aceptada,default);var old=DateTimeOffset.UtcNow.AddDays(-32);
        await d.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET CapturadoEnUtc={old},ExpiraEnUtc={old.AddHours(24)},PrimeraReservaEnUtc={old},
              HorizonteHttpEnUtc={old.AddHours(23)},FinalizadoEnUtc={old.AddHours(1)},UltimoLeaseHastaUtc={old} WHERE Id={f.Id};
            UPDATE dbo.IntentoBienvenida SET ReservadoEnUtc={old},FinalizadoEnUtc={old.AddHours(1)} WHERE EventoId={f.Id};
            """);
        Assert.Equal(1,await new RetencionBienvenidaRepository(d).AplicarAsync(100,default));
        Assert.Null((await f.Evento()).Destinatario);Assert.Empty(await d.Set<IntentoBienvenida>().ToArrayAsync());
        var n=DateTimeOffset.UtcNow;d.Add(EventoBienvenida.CrearPendiente(f.UsuarioId,Epoca,"x@example.invalid",n,n));
        await Assert.ThrowsAsync<DbUpdateException>(()=>d.SaveChangesAsync());
    }
    [C3BSqlFact] public async Task S23_EpocaYEsquemaIncompatiblesCierran()
    {
        await using var f=await F.Crear();await using var d=f.Db();
        await d.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.ControlBienvenida SET EpocaCaptura={Guid.NewGuid()} WHERE Id=1");
        Assert.Null(await f.Repo(d).AdquirirAsync(default));
        await d.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.EventoBienvenida NOCHECK CONSTRAINT CK_Bienvenida_Reserva");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repo(d).AdquirirAsync(default));
        Assert.Equal(0,(await f.Evento()).Reservas);
    }
    [C3BSqlFact] public async Task S24_Rollback51040ConEvidencia()
    {
        await using var f=await F.Crear();await f.Reservar();await using var d=f.Db();
        var ex=await Assert.ThrowsAsync<SqlException>(()=>d.GetService<IMigrator>().MigrateAsync("20261021062129_AddEmailVerification"));
        Assert.Equal(51040,ex.Number);Assert.Equal(1,await d.Set<IntentoBienvenida>().CountAsync());
        Assert.NotNull((await f.Evento()).PayloadUtf8);
    }

    [C3BSqlFact]
    public async Task S25_FalloAntesCommitResultado_RecuperaReservaInciertaSinAceptacion()
    {
        await using var f=await F.Crear();
        var fallo=new FalloCommitResultado(antes:true);
        EventoBienvenida? reservado=null;IntentoBienvenida? intentoPrevio=null;
        var transporte=new TransporteReconocido(async()=>{
            reservado=await f.Evento();
            await using var lectura=f.Db();
            intentoPrevio=await lectura.Set<IntentoBienvenida>().AsNoTracking().SingleAsync();
            fallo.Armado=true;
        });
        await using(var d=f.Db(fallo))
        {
            var proceso=new ProcesarBienvenida(f.Repo(d),
                new PreparadorPayloadBienvenida(Opciones.Perfil!),transporte,TimeProvider.System);
            var ex=await Assert.ThrowsAsync<FalloSimulado>(()=>proceso.EjecutarAsync(default));
            Assert.Same(fallo.Error,ex);
        }
        Assert.Equal(1,fallo.Fallos);Assert.False(fallo.CommitConfirmado);
        Assert.Equal(1,transporte.Calls);Assert.NotNull(reservado);Assert.NotNull(intentoPrevio);
        Assert.Equal(EstadoEventoBienvenida.ReservadoSinResultado,reservado.Estado);
        Assert.Equal(EstadoIntentoBienvenida.ReservadoSinResultado,intentoPrevio.Estado);

        // A fresh context observes rollback, not tracked in-memory objects.
        await using(var lectura=f.Db())
        {
            var e=await f.Evento();
            var i=await lectura.Set<IntentoBienvenida>().AsNoTracking().SingleAsync();
            Assert.Equal(EstadoEventoBienvenida.ReservadoSinResultado,e.Estado);
            Assert.Null(e.IdProveedor);Assert.Null(e.FinalizadoEnUtc);
            Assert.Equal(reservado.LeaseId,e.LeaseId);Assert.Equal(reservado.LeaseGeneracion,e.LeaseGeneracion);
            Assert.Equal(1,e.Reservas);MismaSolicitudDurable(reservado,e);
            Assert.Equal(intentoPrevio.Id,i.Id);Assert.Equal(intentoPrevio.LeaseGeneracion,i.LeaseGeneracion);
            Assert.Equal(EstadoIntentoBienvenida.ReservadoSinResultado,i.Estado);
            Assert.Null(i.HttpIniciadoConfirmado);Assert.Null(i.MotivoEntrega);
            Assert.Null(i.HttpStatus);Assert.Null(i.FinalizadoEnUtc);
        }

        await f.CaducarLease();
        await using(var recuperacion=f.Db())
            Assert.Equal(1,await f.Repo(recuperacion).RecuperarYVencerAsync(100,default));
        await using(var lectura=f.Db())
        {
            var e=await f.Evento();
            var i=await lectura.Set<IntentoBienvenida>().AsNoTracking().SingleAsync();
            Assert.Equal(EstadoIntentoBienvenida.Incierto,i.Estado);
            Assert.Null(i.HttpIniciadoConfirmado);Assert.NotNull(i.FinalizadoEnUtc);
            Assert.Equal(intentoPrevio.Id,i.Id);Assert.Equal(intentoPrevio.LeaseGeneracion,i.LeaseGeneracion);
            Assert.Equal(EstadoEventoBienvenida.EsperandoReintento,e.Estado);
            Assert.True(e.HuboIncertidumbre);Assert.Null(e.IdProveedor);Assert.Null(e.LeaseId);
            Assert.Equal(reservado.LeaseGeneracion,e.LeaseGeneracion);
            Assert.Equal(1,e.Reservas);MismaSolicitudDurable(reservado,e);
            Assert.True(e.ProximaEjecucionEnUtc>=i.ReservadoEnUtc.AddMinutes(1));
            Assert.Equal(reservado.PayloadUtf8,Assert.Single(transporte.Solicitudes).CopiarPayload());
            Assert.Equal(reservado.ClaveIdempotencia,transporte.Solicitudes[0].ClaveIdempotencia);
        }
        Assert.Equal(1,transporte.Calls);
    }

    [C3BSqlFact]
    public async Task S26_CommitResultadoConRespuestaPerdida_NoReadquiereNiReenvia()
    {
        await using var f=await F.Crear();
        var fallo=new FalloCommitResultado(antes:false);
        EventoBienvenida? reservado=null;
        var transporte=new TransporteReconocido(async()=>{
            reservado=await f.Evento();fallo.Armado=true;
        });
        await using(var d=f.Db(fallo))
        {
            var proceso=new ProcesarBienvenida(f.Repo(d),
                new PreparadorPayloadBienvenida(Opciones.Perfil!),transporte,TimeProvider.System);
            var ex=await Assert.ThrowsAsync<FalloSimulado>(()=>proceso.EjecutarAsync(default));
            Assert.Same(fallo.Error,ex);
        }
        Assert.Equal(1,fallo.Fallos);Assert.True(fallo.CommitConfirmado);
        Assert.NotNull(reservado);Assert.Equal(1,transporte.Calls);

        await using(var lectura=f.Db())
        {
            var e=await f.Evento();
            var i=await lectura.Set<IntentoBienvenida>().AsNoTracking().SingleAsync();
            Assert.Equal(EstadoEventoBienvenida.AceptadoProveedor,e.Estado);
            Assert.Equal("opaque-id",e.IdProveedor);Assert.NotNull(e.FinalizadoEnUtc);
            Assert.Equal(1,e.Reservas);Assert.Null(e.LeaseId);MismaSolicitudDurable(reservado,e);
            Assert.Equal(EstadoIntentoBienvenida.AceptadoProveedor,i.Estado);
            Assert.True(i.HttpIniciadoConfirmado);Assert.Equal<short?>(200,i.HttpStatus);
            Assert.Equal((int)MotivoEntregaCorreo.Ninguno,i.MotivoEntrega);
            Assert.NotNull(i.FinalizadoEnUtc);
        }

        // The real orchestrator reconciles from a new context, without manually repeating registration.
        await using(var nuevo=f.Db())
        {
            var repo=f.Repo(nuevo);
            Assert.Null(await repo.AdquirirAsync(default));
            var proceso=new ProcesarBienvenida(repo,
                new PreparadorPayloadBienvenida(Opciones.Perfil!),transporte,TimeProvider.System);
            Assert.Equal(FinProcesamientoBienvenida.SinTrabajo,await proceso.EjecutarAsync(default));
            Assert.Equal(0,await repo.RecuperarYVencerAsync(100,default));
            Assert.Equal(FinProcesamientoBienvenida.SinTrabajo,await proceso.EjecutarAsync(default));
        }
        await using(var lectura=f.Db())
        {
            var e=await f.Evento();
            var i=await lectura.Set<IntentoBienvenida>().AsNoTracking().SingleAsync();
            Assert.Equal(EstadoEventoBienvenida.AceptadoProveedor,e.Estado);
            Assert.Equal("opaque-id",e.IdProveedor);Assert.Equal(1,e.Reservas);
            Assert.Equal(EstadoIntentoBienvenida.AceptadoProveedor,i.Estado);
            Assert.Equal(reservado.LeaseGeneracion,i.LeaseGeneracion);
            MismaSolicitudDurable(reservado,e);
        }
        Assert.Equal(1,transporte.Calls);
    }

    private static void MismaSolicitudDurable(EventoBienvenida antes,EventoBienvenida despues)
    {
        Assert.Equal(antes.Id,despues.Id);Assert.Equal(antes.ClaveIdempotencia,despues.ClaveIdempotencia);
        Assert.Equal(antes.PayloadUtf8,despues.PayloadUtf8);Assert.Equal(antes.PayloadSha256,despues.PayloadSha256);
        Assert.Equal(antes.PerfilProveedorVersion,despues.PerfilProveedorVersion);
        Assert.Equal(antes.PlantillaVersion,despues.PlantillaVersion);
        Assert.Equal(antes.PrimeraReservaEnUtc,despues.PrimeraReservaEnUtc);
        Assert.Equal(antes.HorizonteHttpEnUtc,despues.HorizonteHttpEnUtc);Assert.Equal(antes.ExpiraEnUtc,despues.ExpiraEnUtc);
    }

    private sealed class TransporteReconocido(Func<Task> despuesHttp) : ITransporteCorreoPreparado
    {
        public int Calls;
        public List<SolicitudCorreoPreparada> Solicitudes {get;}=[];
        public async Task<ResultadoEntregaCorreo> EnviarAsync(SolicitudCorreoPreparada s,CancellationToken ct)
        {
            Calls++;Solicitudes.Add(s);ct.ThrowIfCancellationRequested();
            await despuesHttp();return Aceptada;
        }
    }

    private sealed class FalloCommitResultado(bool antes) : DbTransactionInterceptor
    {
        public bool Armado;
        public bool CommitConfirmado;
        public int Fallos;
        public FalloSimulado Error {get;}=new();
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction tx,
            TransactionEventData data,InterceptionResult result,CancellationToken ct=default)
        {
            if(Armado && antes){Armado=false;Fallos++;throw Error;}
            return ValueTask.FromResult(result);
        }
        public override Task TransactionCommittedAsync(DbTransaction tx,TransactionEndEventData data,CancellationToken ct=default)
        {
            if(Armado && !antes){CommitConfirmado=true;Armado=false;Fallos++;throw Error;}
            return Task.CompletedTask;
        }
    }
    private sealed class ControlEnCurso : DbCommandInterceptor
    {
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData data,
            DbDataReader reader,CancellationToken ct=default)
        {
            if(command.CommandText.Contains("FROM dbo.ControlBienvenida WITH (UPDLOCK,HOLDLOCK)",StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(10),ct);
            }
            return reader;
        }
    }
    private sealed class TransporteProhibido : ITransporteCorreoPreparado
    {
        public int Calls;
        public Task<ResultadoEntregaCorreo> EnviarAsync(SolicitudCorreoPreparada s,CancellationToken ct)
        {Calls++;throw new InvalidOperationException("No debe existir HTTP.");}
    }
    private sealed class FalloSimulado : DbException { }
    private sealed class CommitPerdido : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction tx,TransactionEndEventData data,CancellationToken ct=default)=>
            throw new FalloSimulado();
    }
    private sealed class C3BSqlFactAttribute : FactAttribute
    {
        public C3BSqlFactAttribute()
        {
            if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true" ||
                Environment.GetEnvironmentVariable("CONSTIA_AUTHEMAIL01C3B_SQLSERVER_TESTS")!="true" ||
                Environment.GetEnvironmentVariable("CONSTIA_AUTHEMAIL01B_SQLSERVER_TESTS")!="true")
                Skip="Requires explicitly enabled identity-verified disposable C3-B SQL Server 2022 CI.";
        }
    }
}
