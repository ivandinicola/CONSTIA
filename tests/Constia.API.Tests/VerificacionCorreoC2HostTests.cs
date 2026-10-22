using System.Net;
using System.Net.Http.Json;
using Constia.Application.Correo;
using Constia.API.Workers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
namespace Constia.API.Tests;
public sealed class VerificacionCorreoC2HostTests
{
    [Fact]
    public async Task HabilitacionIndependiente_CierraAunqueResendEsteHabilitado()
    {
        using var f=new JwtApiFactory();
        using var host=f.WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(
            new Dictionary<string,string?>{["VerificacionCorreo:C2Enabled"]="false",["Resend:Enabled"]="true"})));
        using var c=host.CreateClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await c.PostAsJsonAsync("/api/usuarios",new{
            nombre="Ana",email="a@example.invalid",password="ABCDE1.@",timeZoneId="Etc/UTC"})).StatusCode);
        Assert.Empty(f.DesafiosCorreo);
        Assert.Equal(TimeSpan.FromSeconds(150),host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HostOptions>>().Value.ShutdownTimeout);
    }
    [Fact]
    public async Task PreparacionFallidaTrasCommit_Conserva201YCuentaYCierraAdmision()
    {
        using var f=new JwtApiFactory();
        using var host=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{
            s.RemoveAll<IPreparadorMensajeVerificacion>();s.AddSingleton<IPreparadorMensajeVerificacion,PreparadorFallido>();
        }));
        using var c=host.CreateClient();
        var r=await c.PostAsJsonAsync("/api/usuarios",new{nombre="Ana",email="a@example.invalid",password="ABCDE1.@",timeZoneId="Etc/UTC"});
        Assert.Equal(HttpStatusCode.Created,r.StatusCode);
        Assert.Single(f.DesafiosCorreo);Assert.Empty(f.CorreosDePrueba);
        Assert.Contains(f.UsuariosPersistidos.Values,u=>u.Email=="a@example.invalid");
        var canal=host.Services.GetRequiredService<ICanalVerificacionCorreo>();
        Assert.False(canal.Disponible);await canal.EsperarVacioAsync(default);
    }
    [Fact]
    public async Task StopAsync_EsperaTrabajoActivoAntesDeTerminar()
    {
        using var f=new JwtApiFactory();var transport=new ReenvioCorreoPrivacidadTests.TransporteBloqueado();
        using var host=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{
            s.RemoveAll<ITransporteCorreoVerificacion>();s.AddSingleton<ITransporteCorreoVerificacion>(transport);
        }));
        using var c=host.CreateClient();
        await c.PostAsJsonAsync("/api/usuarios",new{nombre="Ana",email="a@example.invalid",password="ABCDE1.@",timeZoneId="Etc/UTC"});
        await transport.Iniciado.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var worker=host.Services.GetServices<IHostedService>().OfType<ProcesadorVerificacionCorreo>().Single();
        var stop=worker.StopAsync(default);Assert.False(stop.IsCompleted);
        Assert.False(host.Services.GetRequiredService<ICanalVerificacionCorreo>().Disponible);
        transport.Liberar.TrySetResult();await stop.WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Theory]
    [InlineData("Ana\r\nPrivado","a@example.invalid")]
    [InlineData("Ana","Ana <a@example.invalid>")]
    public async Task DatosIncompatibles_Devuelven400SinEmitir(string nombre,string email)
    {
        using var f=new JwtApiFactory();using var c=f.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/usuarios",new{
            nombre,email,password="ABCDE1.@",timeZoneId="Etc/UTC"})).StatusCode);
        Assert.Empty(f.DesafiosCorreo);
    }
    [Fact]
    public async Task CuatroTrabajosLentos_DrenanEn116SegundosSinVencerEnCola()
    {
        using var f=new JwtApiFactory();var clock=new ColaVerificacionCorreoTests.RelojCorreo();
        var transport=new TransporteLento(clock);
        using var host=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{
            s.RemoveAll<TimeProvider>();s.AddSingleton<TimeProvider>(clock);
            s.RemoveAll<ITransporteCorreoVerificacion>();s.AddSingleton<ITransporteCorreoVerificacion>(transport);
        }));
        using var c=host.CreateClient();
        for(var i=0;i<4;i++)
            Assert.Equal(HttpStatusCode.Created,(await c.PostAsJsonAsync("/api/usuarios",new{
                nombre="Ana",email=$"slow{i}@example.invalid",password="ABCDE1.@",timeZoneId="Etc/UTC"})).StatusCode);
        var worker=host.Services.GetServices<IHostedService>().OfType<ProcesadorVerificacionCorreo>().Single();
        var stop=worker.StopAsync(default);
        for(var i=0;i<4;i++)
        {
            await transport.Inicios.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(stop.IsCompleted);
            clock.Avanzar(TimeSpan.FromSeconds(29));
        }
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4,transport.Aceptados);
        Assert.Equal(TimeSpan.FromSeconds(116),clock.GetElapsedTime(0));
        Assert.Equal(4,f.DesafiosCorreo.Count);
    }
    private sealed class TransporteLento(TimeProvider clock) : ITransporteCorreoVerificacion
    {
        public readonly System.Threading.Channels.Channel<int> Inicios=System.Threading.Channels.Channel.CreateUnbounded<int>();
        public int Aceptados;
        public async Task<ResultadoEntregaCorreo> EnviarAsync(MensajeVerificacionCorreo m,CancellationToken ct)
        {
            var delay=Task.Delay(TimeSpan.FromSeconds(29),clock,ct);
            Inicios.Writer.TryWrite(1);await delay;
            Interlocked.Increment(ref Aceptados);
            return new(EstadoEntregaCorreo.AceptadaPorProveedor,MotivoEntregaCorreo.Ninguno,"ok",intentoHttpIniciado:true);
        }
    }
    [Fact]
    public async Task CancelacionDelProductorDespuesDelCommit_NoCancelaPublicacion()
    {
        using var cancel=new CancellationTokenSource();using var f=new JwtApiFactory();
        using var host=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{
            s.RemoveAll<IPreparadorMensajeVerificacion>();
            s.AddSingleton<IPreparadorMensajeVerificacion>(new PreparadorQueCancela(cancel));
        }));
        using var client=host.CreateClient();
        using var scope=host.Services.CreateScope();
        var service=scope.ServiceProvider.GetRequiredService<Constia.Application.Usuarios.ServicioVerificacionCorreo>();
        var usuario=new Constia.Domain.Usuario("Ana","cancelada@example.invalid","synthetic-hash","Etc/UTC");
        Assert.True(await service.RegistrarAsync(usuario,cancel.Token));
        Assert.True(cancel.IsCancellationRequested);
        await host.Services.GetRequiredService<ICanalVerificacionCorreo>().EsperarVacioAsync(default);
        Assert.Single(f.DesafiosCorreo);Assert.Single(f.CorreosDePrueba);
    }
    private sealed class PreparadorQueCancela(CancellationTokenSource cancel) : IPreparadorMensajeVerificacion
    {
        public MensajeVerificacionCorreo Preparar(Guid id,string e,string n,string t,DateTimeOffset a,DateTimeOffset b)
        {
            cancel.Cancel();
            return new Constia.Infrastructure.Correo.PreparadorMensajeVerificacion(ResendConfiguracionTests.Validas())
                .Preparar(id,e,n,t,a,b);
        }
    }
    [Fact]
    public async Task PlazoAgotado_CierraAdmisionSinLiberarOperacionActiva()
    {
        using var f=new JwtApiFactory();var clock=new ColaVerificacionCorreoTests.RelojCorreo();
        var transport=new TransporteQueIgnoraCancelacion();
        using var host=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{
            s.RemoveAll<TimeProvider>();s.AddSingleton<TimeProvider>(clock);
            s.RemoveAll<ITransporteCorreoVerificacion>();s.AddSingleton<ITransporteCorreoVerificacion>(transport);
        }));
        using var c=host.CreateClient();
        await c.PostAsJsonAsync("/api/usuarios",new{nombre="Ana",email="hung@example.invalid",password="ABCDE1.@",timeZoneId="Etc/UTC"});
        await transport.Iniciado.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var canal=host.Services.GetRequiredService<ICanalVerificacionCorreo>();
        try
        {
            clock.Avanzar(TimeSpan.FromSeconds(30));
            Assert.False(canal.Disponible);
            Assert.False(canal.EsperarVacioAsync(default).IsCompleted);
        }
        finally { transport.Liberar.TrySetResult(); }
        await canal.EsperarVacioAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
    }
    private sealed class TransporteQueIgnoraCancelacion : ITransporteCorreoVerificacion
    {
        public TaskCompletionSource Iniciado=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Liberar=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ResultadoEntregaCorreo> EnviarAsync(MensajeVerificacionCorreo m,CancellationToken ct)
        {
            Iniciado.TrySetResult();await Liberar.Task;
            return new(EstadoEntregaCorreo.AceptadaPorProveedor,MotivoEntregaCorreo.Ninguno,"ok",intentoHttpIniciado:true);
        }
    }
    [Fact]
    public async Task ResultadoCommitIncierto_NoRepiteRegistroNiEnviaAutomaticamente()
    {
        using var f=new JwtApiFactory();var llamadas=0;
        using var host=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{
            s.RemoveAll<Constia.Application.Usuarios.IVerificacionCorreoRepository>();
            s.AddScoped<Constia.Application.Usuarios.IVerificacionCorreoRepository>(sp=>new RepositorioIncierto(
                new CorreoRepositorioFalso(f,sp.GetRequiredService<Constia.Application.Usuarios.IUsuarioPasswordHasher>(),
                    sp.GetRequiredService<TimeProvider>()),()=>Interlocked.Increment(ref llamadas)));
        }));
        using var c=host.CreateClient();
        var r=await c.PostAsJsonAsync("/api/usuarios",new{nombre="Ana",email="uncertain@example.invalid",password="ABCDE1.@",timeZoneId="Etc/UTC"});
        Assert.Equal(HttpStatusCode.ServiceUnavailable,r.StatusCode);
        Assert.Equal(1,llamadas);Assert.Single(f.DesafiosCorreo);Assert.Empty(f.CorreosDePrueba);
        await host.Services.GetRequiredService<ICanalVerificacionCorreo>().EsperarVacioAsync(default);
    }
    private sealed class RepositorioIncierto(Constia.Application.Usuarios.IVerificacionCorreoRepository inner,Action registrar)
        : Constia.Application.Usuarios.IVerificacionCorreoRepository
    {
        public Task<bool> EsquemaDisponibleAsync(CancellationToken ct)=>inner.EsquemaDisponibleAsync(ct);
        public Task<bool> VigenteParaEnvioAsync(Guid id,string email,CancellationToken ct)=>inner.VigenteParaEnvioAsync(id,email,ct);
        public async Task<Constia.Application.Usuarios.ReservaRegistroCorreo> RegistrarAsync(Constia.Domain.Usuario u,byte[] hash,
            Constia.Application.Usuarios.PoliticaVerificacionCorreo politica,CancellationToken ct)
        {
            registrar();await inner.RegistrarAsync(u,hash,politica,ct);
            throw new Constia.Application.Usuarios.CorreoNoDisponibleException();
        }
        public Task<Constia.Application.Usuarios.EmisionCorreo?> ReenviarAsync(string email,byte[] hash,
            Constia.Application.Usuarios.PoliticaVerificacionCorreo politica,CancellationToken ct)=>inner.ReenviarAsync(email,hash,politica,ct);
        public Task<Constia.Application.Usuarios.ResultadoConfirmacionCorreo> ConfirmarAsync(Guid id,byte[] hash,string pass,
            Constia.Application.Usuarios.PoliticaVerificacionCorreo politica,CancellationToken ct)=>inner.ConfirmarAsync(id,hash,pass,politica,ct);
    }

    [Theory]
    [InlineData(401,"",false)]
    [InlineData(403,"",false)]
    [InlineData(429,"daily_quota_exceeded",false)]
    [InlineData(429,"monthly_quota_exceeded",false)]
    [InlineData(401,"",true)]
    [InlineData(403,"",true)]
    [InlineData(429,"daily_quota_exceeded",true)]
    [InlineData(429,"monthly_quota_exceeded",true)]
    public async Task ProveedorNoDisponible_CierraSinProcesarReenviosAdmitidos(int status,string codigo,bool incierto)
    {
        using var f=new JwtApiFactory();var clock=new RelojNotificado();
        var transporte=new ProveedorSimulado(status,codigo,incierto);
        using var host=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{
            s.RemoveAll<TimeProvider>();s.AddSingleton<TimeProvider>(clock);
            s.RemoveAll<ITransporteCorreoVerificacion>();s.AddSingleton<ITransporteCorreoVerificacion>(transporte);
        }));
        using var c=host.CreateClient();
        Assert.Equal(HttpStatusCode.Created,(await c.PostAsJsonAsync("/api/usuarios",new{
            nombre="Ana",email="provider@example.invalid",password="ABCDE1.@",timeZoneId="Etc/UTC"})).StatusCode);
        await transporte.Iniciado.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pendientes=new List<string>();
        for(var i=0;i<3;i++)
        {
            var usuario=new Constia.Domain.Usuario("Ana",$"queued{i}@example.invalid","synthetic-hash","Etc/UTC");
            var hash=new byte[32];hash[0]=(byte)(i+1);
            var desafio=new Constia.Domain.DesafioVerificacionCorreo(usuario.Id,usuario.Email,hash,clock.GetUtcNow().AddMinutes(-1));
            f.UsuariosPersistidos[usuario.Id]=usuario;f.DesafiosCorreo[desafio.Id]=desafio;pendientes.Add(usuario.Email);
        }
        try
        {
            foreach(var email in pendientes)
                Assert.Equal(HttpStatusCode.Accepted,(await c.PostAsJsonAsync("/api/auth/email/reenviar",new{
                    email})).StatusCode);
        }
        finally { transporte.Liberar.TrySetResult(); }
        if(incierto)
        {
            await clock.Reintento.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Avanzar(TimeSpan.FromSeconds(1));
        }
        var canal=host.Services.GetRequiredService<ICanalVerificacionCorreo>();
        await canal.EsperarVacioAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(canal.Disponible);
        Assert.Equal(4,f.DesafiosCorreo.Count);
        Assert.All(f.DesafiosCorreo.Values,d=>Assert.Null(d.InvalidadoEnUtc));
        Assert.Equal(incierto?2:1,transporte.Mensajes.Count);
        Assert.All(transporte.Mensajes,m=>Assert.Same(transporte.Mensajes[0],m));
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await c.PostAsJsonAsync("/api/usuarios",new{
            nombre="Ana",email="blocked@example.invalid",password="ABCDE1.@",timeZoneId="Etc/UTC"})).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await c.PostAsJsonAsync("/api/auth/email/reenviar",new{
            email="provider@example.invalid"})).StatusCode);
        Assert.Equal(4,f.DesafiosCorreo.Count);
    }

    [Theory]
    [InlineData(400,"validation_error")]
    [InlineData(429,"rate_limit_exceeded")]
    public async Task RechazoMensajeOFalloTemporal_NoCierranAdmision(int status,string codigo)
    {
        using var f=new JwtApiFactory();var transporte=new ProveedorSimulado(status,codigo,false);
        transporte.Liberar.TrySetResult();
        using var host=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{
            s.RemoveAll<ITransporteCorreoVerificacion>();s.AddSingleton<ITransporteCorreoVerificacion>(transporte);
        }));
        using var c=host.CreateClient();
        for(var i=0;i<2;i++)
        {
            Assert.Equal(HttpStatusCode.Created,(await c.PostAsJsonAsync("/api/usuarios",new{
                nombre="Ana",email=$"available{i}@example.invalid",password="ABCDE1.@",timeZoneId="Etc/UTC"})).StatusCode);
            var canal=host.Services.GetRequiredService<ICanalVerificacionCorreo>();
            await canal.EsperarVacioAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(canal.Disponible);
        }
        Assert.Equal(2,f.DesafiosCorreo.Count);
        Assert.Equal(2,transporte.Mensajes.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlazoAgotado_CancelacionCooperativaTambienCierraSinNuevasEmisiones(bool retrasarSupervisor)
    {
        using var f=new JwtApiFactory();var clock=new RelojNotificado(retrasarSupervisor);
        var transporte=new TransporteCooperativo();
        using var host=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{
            s.RemoveAll<TimeProvider>();s.AddSingleton<TimeProvider>(clock);
            s.RemoveAll<ITransporteCorreoVerificacion>();s.AddSingleton<ITransporteCorreoVerificacion>(transporte);
        }));
        using var c=host.CreateClient();
        Assert.Equal(HttpStatusCode.Created,(await c.PostAsJsonAsync("/api/usuarios",new{
            nombre="Ana",email="timeout@example.invalid",password="ABCDE1.@",timeZoneId="Etc/UTC"})).StatusCode);
        await transporte.Iniciado.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.Accepted,(await c.PostAsJsonAsync("/api/auth/email/reenviar",new{
            email="timeout@example.invalid"})).StatusCode);
        clock.Avanzar(TimeSpan.FromSeconds(30));
        var canal=host.Services.GetRequiredService<ICanalVerificacionCorreo>();
        await canal.EsperarVacioAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(canal.Disponible);
        Assert.True(transporte.Cancelado);
        Assert.Single(f.DesafiosCorreo);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await c.PostAsJsonAsync("/api/auth/email/reenviar",new{
            email="timeout@example.invalid"})).StatusCode);
    }

    private sealed class ProveedorSimulado(int status,string codigo,bool incierto) : ITransporteCorreoVerificacion
    {
        public TaskCompletionSource Iniciado=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Liberar=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<MensajeVerificacionCorreo> Mensajes=[];
        public async Task<ResultadoEntregaCorreo> EnviarAsync(MensajeVerificacionCorreo mensaje,CancellationToken ct)
        {
            Mensajes.Add(mensaje);Iniciado.TrySetResult();await Liberar.Task.WaitAsync(ct);
            if(incierto && Mensajes.Count==1)
                return new(EstadoEntregaCorreo.ResultadoIncierto,MotivoEntregaCorreo.Timeout,intentoHttpIniciado:true);
            var body=System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new{name=codigo}));
            return Constia.Infrastructure.Correo.ClasificadorRespuestaResend.Clasificar(status,body,TimeSpan.FromSeconds(30));
        }
    }
    private sealed class RelojNotificado(bool retrasarSupervisor=false) : TimeProvider
    {
        private readonly ColaVerificacionCorreoTests.RelojCorreo _inner=new();
        private int _plazos;
        public TaskCompletionSource Reintento=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency=>_inner.TimestampFrequency;
        public override long GetTimestamp()=>_inner.GetTimestamp();
        public override DateTimeOffset GetUtcNow()=>_inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan due,TimeSpan period)
        {
            // Modela la carrera: el callback de cancelación del coordinador se despacha
            // antes que el del supervisor. Su reloj monotónico sí alcanza los 30 segundos.
            if(retrasarSupervisor && due==TimeSpan.FromSeconds(30) && Interlocked.Increment(ref _plazos)==1)
                return new TemporizadorNoDespachado();
            var timer=_inner.CreateTimer(callback,state,due,period);
            if(due==TimeSpan.FromSeconds(1))Reintento.TrySetResult();
            return timer;
        }
        public void Avanzar(TimeSpan tiempo)=>_inner.Avanzar(tiempo);
        private sealed class TemporizadorNoDespachado : ITimer
        {
            public bool Change(TimeSpan due,TimeSpan period)=>true;
            public void Dispose() { }
            public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
        }
    }
    private sealed class TransporteCooperativo : ITransporteCorreoVerificacion
    {
        public TaskCompletionSource Iniciado=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelado;
        public async Task<ResultadoEntregaCorreo> EnviarAsync(MensajeVerificacionCorreo mensaje,CancellationToken ct)
        {
            var espera=Task.Delay(Timeout.InfiniteTimeSpan,ct);Iniciado.TrySetResult();
            try {await espera;throw new InvalidOperationException("Espera inesperada.");}
            catch(OperationCanceledException) when(ct.IsCancellationRequested) {Cancelado=true;throw;}
        }
    }

    private sealed class PreparadorFallido : IPreparadorMensajeVerificacion
    {
        public MensajeVerificacionCorreo Preparar(Guid id,string e,string n,string t,DateTimeOffset a,DateTimeOffset b)
            =>throw new InvalidOperationException("Fallo sintético.");
    }
}
