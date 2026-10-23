using System.Net;
using System.Net.Http.Json;
using Constia.Domain;
using Constia.Infrastructure.Correo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Constia.API.Configuration;
using Constia.API.Workers;
using Constia.Application.Bienvenida;
using Constia.Application.Correo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace Constia.API.Tests;
public sealed class BienvenidaProcesamientoHostTests
{
    [Fact] public void Defaults_NoRegistranWorkerNiActivanEnvio()
    {
        var s=new ServiceCollection();var c=new ConfigurationBuilder().Build();
        s.AddSingleton<IConfiguration>(c);s.AddBienvenidaC3B(c);
        using var p=s.BuildServiceProvider();var o=p.GetRequiredService<OpcionesProcesamientoBienvenida>();
        Assert.False(o.ProcesamientoHabilitado);Assert.False(o.EnvioHabilitado);Assert.False(o.MantenimientoHabilitado);
        Assert.DoesNotContain(s,d=>d.ServiceType==typeof(IHostedService));
    }
    [Fact] public async Task ConfiguracionInvalida_ConservaHostYLogin()
    {
        using var f=new JwtApiFactory();
        using var host=f.WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(
            new Dictionary<string,string?> { ["Bienvenida:ProcesamientoHabilitado"]="incorrecto" })));
        using var c=host.CreateClient();
        Assert.False(host.Services.GetRequiredService<OpcionesProcesamientoBienvenida>().EsValida);
        var r=await c.PostAsJsonAsync("/api/auth/login",new{email=f.Usuario.Email,password=JwtApiFactory.TestPassword});
        Assert.Equal(HttpStatusCode.OK,r.StatusCode);
        Assert.DoesNotContain(host.Services.GetServices<IHostedService>(),h=>h is ProcesadorBienvenida);
    }
    [Fact] public void TransporteC3_NoSustituyeContratoC1()
    {
        using var f=new JwtApiFactory();using var c=f.CreateClient();
        Assert.NotNull(f.Services.GetRequiredService<ITransporteCorreoPreparado>());
        Assert.NotNull(f.Services.GetRequiredService<ITransporteCorreoVerificacion>());
        Assert.NotEqual(f.Services.GetRequiredService<ITransporteCorreoPreparado>().GetType(),
            f.Services.GetRequiredService<ITransporteCorreoVerificacion>().GetType());
    }
    [Fact] public async Task MantenimientoParcialFallido_NoRepiteAntesDelMinuto()
    {
        var r=new SoloMantenimiento();var services=new ServiceCollection();
        services.AddSingleton<IOutboxBienvenidaRepository>(r);
        services.AddSingleton<IRetencionBienvenidaRepository,newRetencion>();
        using var provider=services.BuildServiceProvider();var clock=new Reloj();
        var opciones=OpcionesProcesamientoBienvenida.DesdeValores("false","false","true",null,null);
        using var worker=new ProcesadorBienvenida(provider.GetRequiredService<IServiceScopeFactory>(),opciones,
            new OpcionesResend(),clock,NullLogger<ProcesadorBienvenida>.Instance);
        await worker.StartAsync(default);
        try
        {
            await clock.Demoras.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1,r.Calls);
            clock.Avanzar(TimeSpan.FromSeconds(5));
            await clock.Demoras.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1,r.Calls);
        }
        finally {using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));await worker.StopAsync(stop.Token);}
    }
    private sealed class Reloj : TimeProvider
    {
        private readonly ColaVerificacionCorreoTests.RelojCorreo inner=new();
        public System.Threading.Channels.Channel<bool> Demoras {get;}=global::System.Threading.Channels.Channel.CreateUnbounded<bool>();
        public override long TimestampFrequency=>inner.TimestampFrequency;
        public override long GetTimestamp()=>inner.GetTimestamp();
        public void Avanzar(TimeSpan t)=>inner.Avanzar(t);
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan due,TimeSpan period)
        {
            var timer=inner.CreateTimer(callback,state,due,period);Demoras.Writer.TryWrite(true);return timer;
        }
    }
    private sealed class newRetencion : IRetencionBienvenidaRepository
    {public Task<int> AplicarAsync(int maximo,CancellationToken ct)=>throw new InvalidOperationException("Fallo simulado.");}
    private sealed class SoloMantenimiento : IOutboxBienvenidaRepository
    {
        public int Calls;
        public Task<int> RecuperarYVencerAsync(int m,CancellationToken ct){Interlocked.Increment(ref Calls);return Task.FromResult(1);}
        public Task<TrabajoBienvenida?> AdquirirAsync(CancellationToken ct)=>throw new InvalidOperationException();
        public Task<bool> CongelarAsync(LeaseBienvenida l,byte[] p,string f,CancellationToken ct)=>throw new InvalidOperationException();
        public Task<ReservaBienvenida?> ReservarAsync(LeaseBienvenida l,Guid i,CancellationToken ct)=>throw new InvalidOperationException();
        public Task<PermisoHttpBienvenida?> AutorizarHttpAsync(ReservaBienvenida r,CancellationToken ct)=>throw new InvalidOperationException();
        public Task<bool> RegistrarResultadoAsync(ReservaBienvenida r,ResultadoEntregaCorreo x,CancellationToken ct)=>throw new InvalidOperationException();
        public Task LiberarAsync(LeaseBienvenida l,bool p,MotivoFinalBienvenida? f,CancellationToken ct)=>throw new InvalidOperationException();
        public Task PausarProveedorAsync(ResultadoEntregaCorreo x,CancellationToken ct)=>throw new InvalidOperationException();
    }
}
