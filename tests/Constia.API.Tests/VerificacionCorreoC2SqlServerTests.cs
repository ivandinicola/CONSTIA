using Constia.Application.Correo;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure.Correo;
using Constia.Infrastructure.Security;
using Constia.Infrastructure.Usuarios;
using Microsoft.EntityFrameworkCore;
namespace Constia.API.Tests;

// Nunca ejecutado por la suite segura. Reutiliza exclusivamente la identidad descartable de B.
public sealed class VerificacionCorreoC2SqlServerTests
{
    private const string Password="ABCDE1.@";
    private static readonly AspNetCorePasswordHasher Hasher=new();
    private static VerificacionCorreoRepository Repo(Constia.Infrastructure.ConstiaDbContext db,Reloj clock)=>new(db,Hasher,clock);
    private static Usuario UsuarioNuevo()=>new("Ana",$"{Guid.NewGuid()}@example.invalid",Hasher.HashPassword(Password),"Etc/UTC");
    private static async Task<EmailVerificationSqlDatabase> Crear()
    {
        var d=await EmailVerificationSqlDatabase.CreateAsync();
        try { await using var db=d.CreateContext();await db.Database.MigrateAsync();return d; }
        catch {await d.DisposeAsync();throw;}
    }
    private static async Task<(Usuario Usuario,TokenVerificacion Token,EmisionCorreo Datos)> Alta(EmailVerificationSqlDatabase d,Reloj c)
    {
        await using var db=d.CreateContext();var u=UsuarioNuevo();var t=new GeneradorTokenVerificacion().Generar();
        var r=await Repo(db,c).RegistrarAsync(u,t.Hash,new(),default);
        Assert.Equal(EstadoReservaCorreo.Creado,r.Estado);return(u,t,r.Emision!);
    }
    [C2SqlFact]
    public async Task Registro_DevuelveDatosYFechasPersistidos()
    {
        await using var d=await Crear();var c=new Reloj();var a=await Alta(d,c);
        await using var db=d.CreateContext();var fila=await db.Set<DesafioVerificacionCorreo>().SingleAsync();
        Assert.Equal(fila.Id,a.Datos.DesafioId);Assert.Equal(fila.EmitidoEnUtc,a.Datos.EmitidoEnUtc);
        Assert.Equal(fila.ExpiraEnUtc,a.Datos.ExpiraEnUtc);Assert.Equal("Ana",a.Datos.Nombre);
        Assert.Equal(a.Usuario.Email,a.Datos.Email);Assert.Equal(c.Now,a.Datos.EmitidoEnUtc);
    }
    [C2SqlFact]
    public async Task Reenvio_DevuelveDatosYFechasPersistidos()
    {
        await using var d=await Crear();var c=new Reloj();var a=await Alta(d,c);c.Avanzar(TimeSpan.FromSeconds(60));
        await using var db=d.CreateContext();
        var b=await Repo(db,c).ReenviarAsync(a.Usuario.Email,new GeneradorTokenVerificacion().Generar().Hash,new(),default);
        Assert.NotNull(b);Assert.NotEqual(a.Datos.DesafioId,b.DesafioId);
        var fila=await db.Set<DesafioVerificacionCorreo>().AsNoTracking().SingleAsync(x=>x.Id==b.DesafioId);
        Assert.Equal(c.Now,b.EmitidoEnUtc);Assert.Equal(fila.ExpiraEnUtc,b.ExpiraEnUtc);
        Assert.Equal(a.Usuario.Nombre,b.Nombre);Assert.Equal(a.Usuario.Email,b.Email);
    }
    [C2SqlFact]
    public async Task DestinatarioIncompatible_NoInvalidaNiReserva()
    {
        await using var d=await Crear();var c=new Reloj();var a=await Alta(d,c);c.Avanzar(TimeSpan.FromSeconds(60));
        await using var db=d.CreateContext();var invalido="Ana <a@example.invalid>";
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Usuario] SET [Email]={invalido} WHERE [Id]={a.Usuario.Id}");
        var antes=await db.Set<DesafioVerificacionCorreo>().CountAsync();
        var presupuesto=await db.Database.SqlQueryRaw<long>("SELECT TotalEmisiones AS [Value] FROM PresupuestoEmisionCorreo WHERE Id=1").SingleAsync();
        Assert.Null(await Repo(db,c).ReenviarAsync(invalido,new GeneradorTokenVerificacion().Generar().Hash,new(),default));
        Assert.Equal(antes,await db.Set<DesafioVerificacionCorreo>().CountAsync());
        Assert.Null((await db.Set<DesafioVerificacionCorreo>().SingleAsync()).InvalidadoEnUtc);
        Assert.Equal(presupuesto,await db.Database.SqlQueryRaw<long>("SELECT TotalEmisiones AS [Value] FROM PresupuestoEmisionCorreo WHERE Id=1").SingleAsync());
    }
    [C2SqlFact]
    public async Task ConsultaVigencia_ObservaConfirmacionDesdeOtroContexto()
    {
        await using var d=await Crear();var c=new Reloj();var a=await Alta(d,c);
        await using var lectura=d.CreateContext();var repo=Repo(lectura,c);
        Assert.True(await repo.VigenteParaEnvioAsync(a.Datos.DesafioId,a.Datos.Email,default));
        await using(var escritura=d.CreateContext())
            Assert.Equal(ResultadoConfirmacionCorreo.Confirmado,await Repo(escritura,c).ConfirmarAsync(a.Datos.DesafioId,a.Token.Hash,Password,new(),default));
        Assert.False(await repo.VigenteParaEnvioAsync(a.Datos.DesafioId,a.Datos.Email,default));
    }
    [C2SqlFact]
    public async Task ConsultaVigencia_ObservaReemplazoDesdeOtroContexto()
    {
        await using var d=await Crear();var c=new Reloj();var a=await Alta(d,c);
        await using var lectura=d.CreateContext();var repo=Repo(lectura,c);
        Assert.True(await repo.VigenteParaEnvioAsync(a.Datos.DesafioId,a.Datos.Email,default));c.Avanzar(TimeSpan.FromSeconds(60));
        await using(var escritura=d.CreateContext())
            Assert.NotNull(await Repo(escritura,c).ReenviarAsync(a.Usuario.Email,new GeneradorTokenVerificacion().Generar().Hash,new(),default));
        Assert.False(await repo.VigenteParaEnvioAsync(a.Datos.DesafioId,a.Datos.Email,default));
    }
    [C2SqlFact]
    public async Task ConsultaVigencia_RechazaVencimiento()
    {
        await using var d=await Crear();var c=new Reloj();var a=await Alta(d,c);
        await using var db=d.CreateContext();Assert.True(await Repo(db,c).VigenteParaEnvioAsync(a.Datos.DesafioId,a.Datos.Email,default));
        Assert.False(await Repo(db,c).VigenteParaEnvioAsync(a.Datos.DesafioId,"otro@example.invalid",default));
        c.Avanzar(TimeSpan.FromHours(24));Assert.False(await Repo(db,c).VigenteParaEnvioAsync(a.Datos.DesafioId,a.Datos.Email,default));
    }
    [C2SqlFact]
    public async Task FalloPreparacionTrasCommit_ConservaCuentaDesafioYPresupuesto()
    {
        await using var d=await Crear();var c=new Reloj();await using(var db=d.CreateContext())
        {
            var cola=new ColaVerificacionCorreoMemoria(new());cola.Iniciar();
            var service=new ServicioVerificacionCorreo(Repo(db,c),new GeneradorTokenVerificacion(),cola,
                new PreparadorFallido(),new Activacion(),new(),new(),c);
            Assert.True(await service.RegistrarAsync(UsuarioNuevo(),default));
            Assert.False(cola.Disponible);await cola.EsperarVacioAsync(default);
        }
        await using var check=d.CreateContext();
        Assert.Equal(1,await check.Set<Usuario>().CountAsync());
        Assert.Equal(1,await check.Set<DesafioVerificacionCorreo>().CountAsync());
        Assert.Equal(1L,await check.Database.SqlQueryRaw<long>("SELECT TotalEmisiones AS [Value] FROM PresupuestoEmisionCorreo WHERE Id=1").SingleAsync());
    }
    [C2SqlFact]
    public async Task ReintentoTransporte_NoCreaDesafioNiReserva()
    {
        await using var d=await Crear();var c=new Reloj();var a=await Alta(d,c);
        await using var db=d.CreateContext();var t=new Transporte();
        var preparador=new PreparadorMensajeVerificacion(ResendConfiguracionTests.Validas());
        var mensaje=preparador.Preparar(a.Datos.DesafioId,a.Datos.Email,a.Datos.Nombre,a.Token.Valor,a.Datos.EmitidoEnUtc,a.Datos.ExpiraEnUtc);
        var run=new ProcesarCorreoVerificacion(Repo(db,c),new GeneradorTokenVerificacion(),preparador,t,new(),new(),c)
            .EjecutarAsync(new(c.GetUtcNow(),c.GetTimestamp(),mensaje),default);
        await c.Espera.Task.WaitAsync(TimeSpan.FromSeconds(10));c.Avanzar(TimeSpan.FromSeconds(1));
        Assert.Equal(FinTrabajoCorreo.Aceptado,await run);
        Assert.Equal(2,t.Mensajes.Count);Assert.All(t.Mensajes,m=>Assert.Same(mensaje,m));
        Assert.Equal(1,await db.Set<DesafioVerificacionCorreo>().CountAsync());
        Assert.Equal(1L,await db.Database.SqlQueryRaw<long>("SELECT TotalEmisiones AS [Value] FROM PresupuestoEmisionCorreo WHERE Id=1").SingleAsync());
    }
    [C2SqlFact]
    public async Task Reinicio_ReenvioRespetaIntervaloDe60Segundos()
    {
        await using var d=await Crear();var c=new Reloj();var a=await Alta(d,c);
        // Instancias nuevas de contexto/repositorio: ningún estado de cola sobrevive.
        c.Avanzar(TimeSpan.FromSeconds(59));
        await using(var db=d.CreateContext())
            Assert.Null(await Repo(db,c).ReenviarAsync(a.Usuario.Email,new GeneradorTokenVerificacion().Generar().Hash,new(),default));
        c.Avanzar(TimeSpan.FromSeconds(1));
        await using var despues=d.CreateContext();
        Assert.NotNull(await Repo(despues,c).ReenviarAsync(a.Usuario.Email,new GeneradorTokenVerificacion().Generar().Hash,new(),default));
        Assert.Equal(2,await despues.Set<DesafioVerificacionCorreo>().CountAsync());
    }
    [C2SqlFact]
    public async Task Reinicio_ReenvioConservaLimitesPersistentes()
    {
        await using var d=await Crear();var c=new Reloj();var a=await Alta(d,c);
        for(var i=0;i<4;i++)
        {
            c.Avanzar(TimeSpan.FromSeconds(60));await using var db=d.CreateContext();
            Assert.NotNull(await Repo(db,c).ReenviarAsync(a.Usuario.Email,new GeneradorTokenVerificacion().Generar().Hash,new(),default));
        }
        c.Avanzar(TimeSpan.FromSeconds(60));await using var check=d.CreateContext();
        Assert.Null(await Repo(check,c).ReenviarAsync(a.Usuario.Email,new GeneradorTokenVerificacion().Generar().Hash,new(),default));
        Assert.Equal(5,await check.Set<DesafioVerificacionCorreo>().CountAsync());
        var otro=UsuarioNuevo();
        Assert.Equal(EstadoReservaCorreo.PresupuestoAgotado,(await Repo(check,c).RegistrarAsync(otro,
            new GeneradorTokenVerificacion().Generar().Hash,new(){EmisionesGlobales=5},default)).Estado);
        Assert.Equal(1,await check.Set<Usuario>().CountAsync());
    }
    private sealed class C2SqlFactAttribute : FactAttribute
    {
        public C2SqlFactAttribute()
        {
            if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true" ||
               Environment.GetEnvironmentVariable("CONSTIA_AUTHEMAIL01C2_SQLSERVER_TESTS")!="true")
                Skip="Requires explicitly enabled disposable C2 SQL CI.";
        }
    }
    private sealed class Activacion : IActivacionCorreo {public Task<bool> HabilitadoAsync(CancellationToken ct)=>Task.FromResult(true);}
    private sealed class PreparadorFallido : IPreparadorMensajeVerificacion
    {
        public MensajeVerificacionCorreo Preparar(Guid id,string e,string n,string t,DateTimeOffset a,DateTimeOffset b)
            =>throw new InvalidOperationException("Fallo sintético.");
    }
    private sealed class Transporte : ITransporteCorreoVerificacion
    {
        public List<MensajeVerificacionCorreo> Mensajes=[];
        public Task<ResultadoEntregaCorreo> EnviarAsync(MensajeVerificacionCorreo m,CancellationToken ct)
        {
            Mensajes.Add(m);
            return Task.FromResult(Mensajes.Count==1
                ? new ResultadoEntregaCorreo(EstadoEntregaCorreo.FalloTemporal,MotivoEntregaCorreo.ErrorProveedor)
                : new ResultadoEntregaCorreo(EstadoEntregaCorreo.AceptadaPorProveedor,MotivoEntregaCorreo.Ninguno,"ok",intentoHttpIniciado:true));
        }
    }
    private sealed class Reloj : TimeProvider
    {
        public DateTimeOffset Now=DateTimeOffset.Parse("2026-10-22T00:00:00Z");
        private long _ticks;private readonly List<Temporizador> _timers=[];
        public TaskCompletionSource Espera=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow()=>Now;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Interlocked.Read(ref _ticks);
        public void Avanzar(TimeSpan t)
        {
            Now=Now.Add(t);Interlocked.Add(ref _ticks,t.Ticks);
            Temporizador[] timers;lock(_timers) timers=_timers.ToArray();foreach(var timer in timers)timer.Disparar();
        }
        public override ITimer CreateTimer(TimerCallback cb,object? state,TimeSpan due,TimeSpan period)
        {
            var timer=new Temporizador(this,cb,state,due);lock(_timers)_timers.Add(timer);
            if(due==TimeSpan.FromSeconds(1))Espera.TrySetResult();return timer;
        }
        private sealed class Temporizador(Reloj c,TimerCallback cb,object? state,TimeSpan due) : ITimer
        {
            private long _vence=c.GetTimestamp()+due.Ticks;private int _disposed;
            public void Disparar(){if(c.GetTimestamp()>=_vence&&Interlocked.Exchange(ref _disposed,1)==0)cb(state);}
            public bool Change(TimeSpan due,TimeSpan period){_vence=c.GetTimestamp()+due.Ticks;return true;}
            public void Dispose()=>Interlocked.Exchange(ref _disposed,1);
            public ValueTask DisposeAsync(){Dispose();return ValueTask.CompletedTask;}
        }
    }
}
