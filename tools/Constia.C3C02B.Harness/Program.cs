using Constia.API.Configuration;
using Constia.Application.Bienvenida;
using Constia.Application.Correo;
using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Bienvenida;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace Constia.C3C02B.Harness;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if(args.Length!=1)throw new InvalidOperationException("One explicit preflight command required.");
            var command=args[0];
            var role=command switch {"isolation-peer" or "peer-health"=>"peer",
                "isolation-denied" or "denied-health"=>"denied","isolation-only" or "isolation-controls"=>"app",
                _=>throw new InvalidOperationException("Operational entry is blocked.")};
            var identity=PreflightIdentity.Load(role);
            if(command is "isolation-peer" or "isolation-denied")
            {
                await RunReceiver(identity,role);return 0;
            }
            if(command is "peer-health" or "denied-health")
            {
                await ConnectSynthetic(identity,"127.0.0.1",1433,role);Console.WriteLine("PEER_HEALTHY");return 0;
            }
            if(command=="isolation-controls")
            {
                if(identity.ProofStage!="prepared")throw new InvalidOperationException("Controls must precede firewall.");
                await ConnectSynthetic(identity,identity.PeerIp,1433,"peer");
                await ConnectSynthetic(identity,identity.DeniedIp,1433,"denied");
                await ConnectSynthetic(identity,identity.Gateway,identity.GatewayPort,"gateway");
                Console.WriteLine("CONTROLS_POSITIVE");return 0;
            }
            if(identity.ProofStage!="isolated")throw new InvalidOperationException("Firewall proof required.");
            await VerifyIsolationTransport();
            await ConnectSynthetic(identity,identity.PeerIp,1433,"peer");
            await RequireBlocked(identity,identity.DeniedIp,1433,"denied");
            await RequireBlocked(identity,identity.Gateway,identity.GatewayPort,"gateway");
            Console.WriteLine("ISOLATION_PASSED_NO_SQL_NO_WORKERS");return 0;
        }
        catch {Console.Error.WriteLine("PREFLIGHT_FAILED");return 1;}
    }
    public static IHost BuildIsolation(string? phase,string transport="simulated",bool worker=false)
    {
        PreflightIdentity.RequirePhase(phase);
        if(transport!="simulated" || worker)throw new InvalidOperationException("Isolation cannot use workers or real transport.");
        var b=Host.CreateApplicationBuilder(new HostApplicationBuilderSettings{DisableDefaults=true});
        b.Logging.ClearProviders();
        // Only the two named simulated clients: no DbContext, repository, processor or hosted service registration.
        b.Services.ConfigureHttpClientDefaults(c=>c.ConfigurePrimaryHttpMessageHandler(()=>new SimulatedResendHandler(new())).RemoveAllLoggers());
        foreach(var name in new[]{ResendC1ServiceCollectionExtensions.NombreCliente,BienvenidaC3BServiceCollectionExtensions.NombreCliente})
            b.Services.AddHttpClient(name,c=>c.Timeout=Timeout.InfiniteTimeSpan);
        return b.Build();
    }
    public static async Task VerifyIsolationTransport()
    {
        using var host=BuildIsolation(PreflightIdentity.Isolation);
        var factory=host.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        var clients=host.Services.GetRequiredService<IHttpClientFactory>();
        foreach(var name in new[]{ResendC1ServiceCollectionExtensions.NombreCliente,BienvenidaC3BServiceCollectionExtensions.NombreCliente})
        {
            var handler=factory.CreateHandler(name);
            while(handler is DelegatingHandler wrapper)handler=wrapper.InnerHandler!;
            if(handler is not SimulatedResendHandler fake)throw new InvalidOperationException("Real handler prohibited.");
            using var client=clients.CreateClient(name);
            var transport=new Constia.Infrastructure.Correo.ResendHttpTransport(client,new(){Enabled=true,
                ApiKey=SimulatedResendHandler.Key,FromAddress="correo@example.invalid",FromName="CONSTIA",RequestTimeoutSeconds=10},TimeProvider.System);
            var bytes=System.Text.Encoding.UTF8.GetBytes("""{"from":"CONSTIA <correo@example.invalid>","to":["synthetic@example.invalid"],"subject":"synthetic","html":"synthetic","text":"synthetic"}""");
            var result=await transport.EnviarAsync(new("synthetic-isolation-key",bytes),default);
            if(result.Estado!=EstadoEntregaCorreo.AceptadaPorProveedor || fake.Calls!=1)
                throw new InvalidOperationException("Simulated response missing.");
        }
    }
    private static async Task RunReceiver(PreflightIdentity identity,string role)
    {
        var listener=new TcpListener(IPAddress.Any,1433);listener.Start();
        try
        {
            File.WriteAllText("/run/c3/receiver-ready","ready");
            while(true)
            {
                using var client=await listener.AcceptTcpClientAsync();
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var stream=client.GetStream();var bytes=new byte[32];
                try
                {
                    await stream.ReadExactlyAsync(bytes,timeout.Token);
                    if(System.Text.Encoding.ASCII.GetString(bytes)!=identity.Nonce)continue;
                    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("c3c02b:"+identity.Nonce+":"+role),timeout.Token);
                }
                catch(OperationCanceledException) { }
            }
        }
        finally{listener.Stop();}
    }
    private static async Task ConnectSynthetic(PreflightIdentity identity,string ip,int port,string role)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var client=new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Parse(ip),port,timeout.Token);
        var stream=client.GetStream();await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(identity.Nonce),timeout.Token);
        var expected=System.Text.Encoding.ASCII.GetBytes("c3c02b:"+identity.Nonce+":"+role);var reply=new byte[expected.Length];
        await stream.ReadExactlyAsync(reply,timeout.Token);
        if(!reply.SequenceEqual(expected))throw new InvalidOperationException("Synthetic receiver identity mismatch.");
    }
    private static async Task RequireBlocked(PreflightIdentity identity,string ip,int port,string role)
    {
        // Any successful TCP connect is a failure, even if the receiver sends unexpected bytes.
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var client=new TcpClient(AddressFamily.InterNetwork);
        try{await client.ConnectAsync(IPAddress.Parse(ip),port,timeout.Token);}
        catch(SocketException){return;}
        catch(OperationCanceledException) when(timeout.IsCancellationRequested){return;}
        throw new InvalidOperationException("Forbidden receiver reachable.");
    }
    private static async Task<int> OperationalPrepared(string[] args)
    {
        PreflightIdentity.RequirePhase("operational-full"); // Always blocked before loading SQL identity or constructing services.
        try
        {
            var identity=SandboxIdentity.Load();
            if (args.Length == 3 && args[0] == "probe")
            {
                using var sentinel=System.Text.Json.JsonDocument.Parse(File.ReadAllText("/run/c3/sentinel.json"));
                if (!IPAddress.TryParse(args[1],out var ip) || args[1]!=identity.Gateway ||
                    !int.TryParse(args[2],out var port) || port is <1024 or >65535 ||
                    sentinel.RootElement.GetProperty("gateway").GetString()!=args[1] ||
                    sentinel.RootElement.GetProperty("port").GetInt32()!=port) return 2;
                using var socket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);
                try { await socket.ConnectAsync(ip,port).WaitAsync(TimeSpan.FromSeconds(2)); return 2; }
                catch (SocketException) { return 0; }
                catch (TimeoutException) { return 0; }
            }
            if (args is ["verify"])
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(250));
                while (true)
                {
                    try { await using var c=identity.Connection("admin","master"); await c.OpenAsync(timeout.Token); await identity.Verify(c,"master",timeout.Token); return 0; }
                    catch (Microsoft.Data.SqlClient.SqlException) when (!timeout.IsCancellationRequested) { await timer.WaitForNextTickAsync(timeout.Token); }
                }
            }
            if (args.Length!=3 || args[0]!="worker" || !new[]{"defaults","send","maintenance","partial","changed-profile"}.Contains(args[1]) ||
                (args[2]!="none" && !PhaseGate.Names.Contains(args[2]))) return 2;
            var gate=new PhaseGate(args[2]=="none"?null:args[2]);
            FileStream singleton;
            try {singleton=new FileStream("/run/c3/worker.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException) {Console.Error.WriteLine("SINGLETON_REJECTED");return 3;}
            using var singletonLifetime=singleton;
            using var host=Build(identity,args[1],gate);
            gate.StartInputPump();
            await host.StartAsync();
            await gate.Reach("started",default);
            await gate.Stop.Task.WaitAsync(TimeSpan.FromMinutes(5));
            await host.StopAsync(TimeSpan.FromSeconds(20));
            Console.WriteLine("STOPPED");
            return 0;
        }
        catch { Console.Error.WriteLine("HARNESS_FAILED"); return 1; }
    }
    public static IHost Build(SandboxIdentity identity,string mode,PhaseGate gate,string response="accepted")
    {
        identity.Validate();
        var b=Host.CreateApplicationBuilder(new HostApplicationBuilderSettings{DisableDefaults=true});
        b.Logging.ClearProviders();
        b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{
            ["Bienvenida:ProcesamientoHabilitado"]=mode is "send" or "partial" or "changed-profile"?"true":"false",
            ["Bienvenida:EnvioHabilitado"]=mode is "send" or "changed-profile"?"true":"false",
            ["Bienvenida:MantenimientoHabilitado"]=mode=="maintenance"?"true":"false",
            ["Bienvenida:CuentaProveedor"]=mode=="partial"?null:mode=="changed-profile"?"synthetic-other-account":"synthetic-account",
            ["Resend:Enabled"]="true",["Resend:ApiKey"]=SimulatedResendHandler.Key,
            ["Resend:FromAddress"]="correo@example.invalid",["Resend:FromName"]="CONSTIA",
            ["VerificacionCorreo:C2Enabled"]="false",["Bienvenida:CapturarEventos"]="false"});
        b.Services.AddSingleton(TimeProvider.System);
        b.Services.AddDbContext<ConstiaDbContext>(o=>o.UseSqlServer(identity.Connection("runtime",identity.Database).ConnectionString)
            .AddInterceptors(new IdentityConnectionInterceptor(identity)));
        b.Services.AddResendC1(b.Configuration);
        b.Services.AddBienvenidaC3A(b.Configuration);
        b.Services.AddBienvenidaC3B(b.Configuration);
        // Override BOTH named clients; no production socket-backed primary handler survives composition.
        foreach (var name in new[]{ResendC1ServiceCollectionExtensions.NombreCliente,BienvenidaC3BServiceCollectionExtensions.NombreCliente})
            b.Services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(()=>new SimulatedResendHandler(gate,response));
        b.Services.Replace(ServiceDescriptor.Scoped<IOutboxBienvenidaRepository>(sp=>
            new ObservedRepository(new OutboxBienvenidaRepository(sp.GetRequiredService<ConstiaDbContext>(),
                sp.GetRequiredService<OpcionesProcesamientoBienvenida>()),gate)));
        return b.Build();
    }
}

public sealed class ObservedRepository(IOutboxBienvenidaRepository inner,PhaseGate gate) : IOutboxBienvenidaRepository
{
    public async Task<TrabajoBienvenida?> AdquirirAsync(CancellationToken ct) { var x=await inner.AdquirirAsync(ct);if(x!=null)await gate.Reach("acquired",ct);return x; }
    public async Task<bool> CongelarAsync(LeaseBienvenida l,byte[] p,string f,CancellationToken ct) {
        try {var x=await inner.CongelarAsync(l,p,f,ct);if(x)await gate.Reach("frozen",ct);return x;}
        catch(Microsoft.Data.SqlClient.SqlException) {await gate.Reach("sql-error",default);throw;}
    }
    public async Task<ReservaBienvenida?> ReservarAsync(LeaseBienvenida l,Guid id,CancellationToken ct) {var x=await inner.ReservarAsync(l,id,ct);if(x!=null)await gate.Reach("reserved",ct);return x;}
    public async Task<PermisoHttpBienvenida?> AutorizarHttpAsync(ReservaBienvenida r,CancellationToken ct) {var x=await inner.AutorizarHttpAsync(r,ct);if(x!=null)await gate.Reach("authorized",ct);return x;}
    public async Task<bool> RegistrarResultadoAsync(ReservaBienvenida r,ResultadoEntregaCorreo v,CancellationToken ct) {
        try {var x=await inner.RegistrarResultadoAsync(r,v,ct);if(x)await gate.Reach("recorded",ct);return x;}
        catch(Microsoft.Data.SqlClient.SqlException) {await gate.Reach("sql-error",default);throw;}
    }
    public async Task LiberarAsync(LeaseBienvenida l,bool p,MotivoFinalBienvenida? f,CancellationToken ct) {await inner.LiberarAsync(l,p,f,ct);await gate.Reach("released",ct);}
    public async Task<int> RecuperarYVencerAsync(int max,CancellationToken ct) {var n=await inner.RecuperarYVencerAsync(max,ct);await gate.Reach("maintenance",ct);return n;}
    public Task PausarProveedorAsync(ResultadoEntregaCorreo r,CancellationToken ct)=>inner.PausarProveedorAsync(r,ct);
}
