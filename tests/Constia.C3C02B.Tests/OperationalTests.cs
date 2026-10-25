using System.Diagnostics;
using System.Threading.Channels;
using System.Text.Json;
using Constia.C3C02B.Harness;
using Constia.Domain;
using Constia.Infrastructure.Bienvenida;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Constia.C3C02B.Tests;

internal sealed class Child : IAsyncDisposable
{
    private readonly Process process;
    private readonly Channel<string> phases=Channel.CreateUnbounded<string>();
    private readonly Task pump;
    public List<string> Http { get; } = [];
    private Child(Process p)
    {
        process=p;
        pump=Task.Run(async()=>{
            try {
                while(await p.StandardOutput.ReadLineAsync() is { } line) {
                    if(line.StartsWith("HTTP:")) { lock(Http) Http.Add(line); }
                    else if(line=="STOPPED" || line.StartsWith("PHASE:")) await phases.Writer.WriteAsync(line);
                    else throw new InvalidOperationException("Unexpected process output.");
                }
                phases.Writer.TryComplete();
            } catch(Exception e) { phases.Writer.TryComplete(e); }
        });
    }
    public static Child Start(string mode,string phase)
    {
        var dll=File.Exists("/app/harness/Constia.C3C02B.Harness.dll")
            ? "/app/harness/Constia.C3C02B.Harness.dll" : throw new InvalidOperationException("Isolated child assembly absent.");
        var p=new Process{StartInfo=new("dotnet"){RedirectStandardInput=true,RedirectStandardOutput=true,
            RedirectStandardError=true,UseShellExecute=false}};
        foreach(var a in new[]{dll,"worker",mode,phase})p.StartInfo.ArgumentList.Add(a);
        if(!p.Start())throw new InvalidOperationException("Process not started.");
        return new(p);
    }
    public async Task At(string phase)
    {
        using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await foreach(var value in phases.Reader.ReadAllAsync(ct.Token))
            if(value=="PHASE:"+phase)return;
        throw new InvalidOperationException("Process ended before the barrier.");
    }
    public Task Continue(string phase)=>process.StandardInput.WriteLineAsync("continue:"+phase);
    public async Task Stop()
    {
        await process.StandardInput.WriteLineAsync("stop");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25));
        await pump;
        Assert.Equal(0,process.ExitCode);
    }
    public async Task Kill()
    {
        process.Kill(entireProcessTree:true);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await pump;
    }
    public async Task ExpectSingletonRejection()
    {
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));await pump;
        Assert.Equal(3,process.ExitCode);
        Assert.Equal("SINGLETON_REJECTED",(await process.StandardError.ReadToEndAsync()).Trim());
        Assert.Empty(Http);
    }
    public async ValueTask DisposeAsync()
    {
        if(!process.HasExited)await Kill();
        process.Dispose();
    }
}

[Collection("Sandbox")]
public sealed class OperationalTests
{

    [Fact,Trait("Category","Operational")]
    public async Task SecondCoordinatorIsRejectedBeforeStartingWork()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();await f.Seed();
        var before=await f.Snapshot();
        await using var first=Child.Start("defaults","started");await first.At("started");
        await using(var second=Child.Start("defaults","none"))await second.ExpectSingletonRejection();
        await first.Continue("started");await first.Stop();
        Assert.Equal(before,await f.Snapshot());
    }
    [Fact,Trait("Category","Operational")]
    public async Task RestartWithChangedProfilePausesWithoutChangingFrozenBytes()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();var id=await f.Seed();
        await using(var c=Child.Start("send","frozen")){await c.At("frozen");await c.Kill();Assert.Empty(c.Http);}
        var old=await f.Event(id);await LeaseExpires(f,id);
        await using(var d=f.Db())await f.Repo(d).RecuperarYVencerAsync(100,default);
        await using var next=Child.Start("changed-profile","released");await next.At("released");
        await next.Continue("released");await next.Stop();
        var changed=await f.Event(id);
        Assert.Equal(EstadoEventoBienvenida.Pausado,changed.Estado);
        Assert.Equal(old.PayloadUtf8,changed.PayloadUtf8);Assert.Equal(old.PerfilProveedorVersion,changed.PerfilProveedorVersion);
        Assert.Equal(old.ClaveIdempotencia,changed.ClaveIdempotencia);Assert.Empty(next.Http);
    }
    [Fact,Trait("Category","Operational")]
    public async Task RateLimitPersistsRetryAfterWithoutProviderPause()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();var id=await f.Seed();
        using(var host=Constia.C3C02B.Harness.Program.Build(f.Identity,"send",new(),"rate")) {
            using var scope=host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<Constia.Application.Bienvenida.ProcesarBienvenida>().EjecutarAsync(default);
        }
        var e=await f.Event(id);Assert.Equal(EstadoEventoBienvenida.EsperandoReintento,e.Estado);
        Assert.True(e.ProximaEjecucionEnUtc>=e.PrimeraReservaEnUtc!.Value.AddMinutes(2));
        await using var d=f.Db("migrator");
        Assert.False((await d.Set<ControlBienvenida>().AsNoTracking().SingleAsync()).PausaProveedor);
    }
    [Fact,Trait("Category","Operational")]
    public async Task TenSecondExpiryMarginPreventsAcquisition()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();var id=await f.Seed();
        await using var d=f.Db("migrator");var now=await DisposableSqlDatabase.Now(d);
        var capture=now.AddHours(-24).AddSeconds(10);
        await d.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET CapturadoEnUtc={capture},ConfirmadoEnUtc={capture},
                ExpiraEnUtc={capture.AddHours(24)},ProximaEjecucionEnUtc={capture} WHERE Id={id}
            """);
        Assert.Null(await f.Repo(d).AdquirirAsync(default));
        Assert.Equal(0,(await f.Event(id)).Reservas);
    }

    private static async Task Fault(DisposableSqlDatabase f,string action)
    {
        if(action is not ("disconnect" or "reconnect"))throw new InvalidOperationException("Unknown fault.");
        var id=Guid.NewGuid().ToString("N");
        var payload=JsonSerializer.Serialize(new{id,action,nonce=f.Identity.Nonce});
        var tmp="/run/c3/request-"+id+".tmp";
        await File.WriteAllTextAsync(tmp,payload);
        File.Move(tmp,"/run/c3/request.json",true);
        using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while(true) {
            if(File.Exists("/run/c3/ack.json")) {
                using var doc=JsonDocument.Parse(await File.ReadAllTextAsync("/run/c3/ack.json",ct.Token));
                if(doc.RootElement.GetProperty("id").GetString()==id) {
                    Assert.Equal("ok",doc.RootElement.GetProperty("status").GetString());return;
                }
            }
            await timer.WaitForNextTickAsync(ct.Token);
        }
    }
    [Fact,Trait("Category","Operational")]
    public async Task SqlLossBeforeAuthorizationNeverInvokesHandler()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();var id=await f.Seed();
        await using var c=Child.Start("send","acquired");await c.At("acquired");
        try {
            await Fault(f,"disconnect");await c.Continue("acquired");await c.At("sql-error");
            Assert.Empty(c.Http);await c.Kill();
        } finally {await Fault(f,"reconnect");}
        Assert.Equal(0,(await f.Event(id)).Reservas);
    }
    [Fact,Trait("Category","Operational")]
    public async Task SqlLossAfterFakeHttpDoesNotAssumeAcceptedCommit()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();var id=await f.Seed();
        await using var c=Child.Start("send","response");await c.At("response");
        try {
            await Fault(f,"disconnect");await c.Continue("response");await c.At("sql-error");
            Assert.Single(c.Http);await c.Kill();
        } finally {await Fault(f,"reconnect");}
        Assert.Equal(EstadoEventoBienvenida.ReservadoSinResultado,(await f.Event(id)).Estado);
        await LeaseExpires(f,id);
        await using(var d=f.Db())await f.Repo(d).RecuperarYVencerAsync(100,default);
        Assert.True((await f.Event(id)).HuboIncertidumbre);
    }
    [Fact,Trait("Category","Operational")]
    public async Task TwoRealStartsKeepDefaultsClosed()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();var id=await f.Seed();
        var before=await f.Snapshot();
        for(var k=0;k<2;k++) {
            await using var c=Child.Start("defaults","none");await c.At("started");await c.Stop();Assert.Empty(c.Http);
        }
        Assert.Equal(before,await f.Snapshot());
        Assert.Equal(0,(await f.Event(id)).Reservas);
    }
    [Fact,Trait("Category","Operational")]
    public async Task PartialConfigurationDoesNotReserveOrSend()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();await f.Seed();
        var before=await f.Snapshot();
        await using var c=Child.Start("partial","none");await c.At("started");await c.Stop();
        Assert.Empty(c.Http);Assert.Equal(before,await f.Snapshot());
    }
    [Fact,Trait("Category","Operational")]
    public async Task MaintenanceWorkerDoesNotReserveOrSend()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();var id=await f.Seed();
        // Coherent expired Pending event: preserve the exact 24h invariant.
        await using(var d=f.Db("migrator")) await d.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET CapturadoEnUtc=DATEADD(day,-2,CapturadoEnUtc),
                ConfirmadoEnUtc=DATEADD(day,-2,ConfirmadoEnUtc),ExpiraEnUtc=DATEADD(day,-2,ExpiraEnUtc),
                ProximaEjecucionEnUtc=DATEADD(day,-2,ProximaEjecucionEnUtc) WHERE Id={id}
            """);
        await using var c=Child.Start("maintenance","maintenance");await c.At("maintenance");await c.Continue("maintenance");
        // Stop waits for the current maintenance cycle to finish.
        await c.Stop();var e=await f.Event(id);Assert.Empty(c.Http);Assert.Equal(0,e.Reservas);
        Assert.NotEqual(EstadoEventoBienvenida.Pendiente,e.Estado);
    }
    private static async Task LeaseExpires(DisposableSqlDatabase f,Guid id)
    {
        var limit=(await f.Event(id)).LeaseHastaUtc!.Value;
        using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(105));
        using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while(true) {
            await using var d=f.Db("migrator");
            if(await DisposableSqlDatabase.Now(d)>limit)return;
            await timer.WaitForNextTickAsync(ct.Token);
        }
    }
    [Fact,Trait("Category","Operational")]
    public async Task CrashBeforeReservationRecoversWithNewGeneration()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();var id=await f.Seed();
        await using(var c=Child.Start("send","acquired")) {await c.At("acquired");Assert.Empty(c.Http);await c.Kill();}
        var old=await f.Event(id);Assert.Equal(0,old.Reservas);await LeaseExpires(f,id);
        await using(var d=f.Db())Assert.Equal(1,await f.Repo(d).RecuperarYVencerAsync(100,default));
        await using var next=Child.Start("send","acquired");await next.At("acquired");
        Assert.True((await f.Event(id)).LeaseGeneracion>old.LeaseGeneracion);
        Assert.Empty(next.Http);await next.Kill();
    }
    [Fact,Trait("Category","Operational")]
    public async Task CrashAfterReservationPreservesUncertaintyAndFrozenRequest()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();var id=await f.Seed();
        await using(var c=Child.Start("send","reserved")) {await c.At("reserved");Assert.Empty(c.Http);await c.Kill();}
        var old=await f.Event(id);Assert.Equal(1,old.Reservas);
        await LeaseExpires(f,id);
        await using(var d=f.Db())Assert.Equal(1,await f.Repo(d).RecuperarYVencerAsync(100,default));
        var recovered=await f.Event(id);Assert.True(recovered.HuboIncertidumbre);
        Assert.Equal(old.PayloadUtf8,recovered.PayloadUtf8);Assert.Equal(old.ClaveIdempotencia,recovered.ClaveIdempotencia);
        Assert.Equal(old.PerfilProveedorVersion,recovered.PerfilProveedorVersion);
        // First retry is already eligible after the real 90s lease expires.
        await using var next=Child.Start("send","recorded");await next.At("recorded");await next.Stop();
        var final=await f.Event(id);Assert.Equal(EstadoEventoBienvenida.AceptadoProveedor,final.Estado);
        Assert.True(final.HuboIncertidumbre);Assert.Single(next.Http);
        Assert.Equal(old.PayloadUtf8,final.PayloadUtf8);Assert.Equal(old.ClaveIdempotencia,final.ClaveIdempotencia);
    }
    [Fact,Trait("Category","Operational")]
    public async Task CooperativeStopPersistsUncertainHandlerResult()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();var id=await f.Seed();
        await using var c=Child.Start("send","handler");await c.At("handler");await c.Stop();
        Assert.Single(c.Http);var e=await f.Event(id);Assert.True(e.HuboIncertidumbre);
        Assert.NotEqual(EstadoEventoBienvenida.AceptadoProveedor,e.Estado);
        Assert.Null(e.LeaseId);
    }
    [Fact,Trait("Category","Operational")]
    public async Task ForcedStopDuringHandlerRetainsDurableReservation()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();var id=await f.Seed();
        await using(var c=Child.Start("send","handler")) {await c.At("handler");Assert.Single(c.Http);await c.Kill();}
        Assert.Equal(EstadoEventoBienvenida.ReservadoSinResultado,(await f.Event(id)).Estado);
        await LeaseExpires(f,id);
        await using(var d=f.Db())await f.Repo(d).RecuperarYVencerAsync(100,default);
        Assert.True((await f.Event(id)).HuboIncertidumbre);
    }
    [Fact,Trait("Category","Operational")]
    public async Task PauseAfterPermissionDoesNotRevokeExistingPermission()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();var id=await f.Seed();
        await using var c=Child.Start("send","authorized");await c.At("authorized");
        await using(var d=f.Db("migrator"))await d.Database.ExecuteSqlRawAsync("UPDATE dbo.ControlBienvenida SET EnvioHabilitado=0 WHERE Id=1");
        await c.Continue("authorized");await c.At("recorded");await c.Stop();
        Assert.Single(c.Http);Assert.Equal(EstadoEventoBienvenida.AceptadoProveedor,(await f.Event(id)).Estado);
        await f.Seed();await using var d2=f.Db();Assert.Null(await f.Repo(d2).AdquirirAsync(default));
    }
    [Fact,Trait("Category","Operational")]
    public async Task TlsFailurePausesDurablyWithoutSecondPermission()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();var id=await f.Seed();
        using(var host=Constia.C3C02B.Harness.Program.Build(f.Identity,"send",new(),"tls")) {
            using var scope=host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<Constia.Application.Bienvenida.ProcesarBienvenida>().EjecutarAsync(default);
        }
        await using var d=f.Db("migrator");
        Assert.True((await d.Set<ControlBienvenida>().AsNoTracking().SingleAsync()).PausaProveedor);
        Assert.Null(await f.Repo(d).AdquirirAsync(default));
        Assert.NotEqual(EstadoEventoBienvenida.AceptadoProveedor,(await f.Event(id)).Estado);
    }
}
