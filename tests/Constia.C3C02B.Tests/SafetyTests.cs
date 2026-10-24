using System.Net;
using System.Text;
using Constia.C3C02B.Harness;
using Constia.Application.Bienvenida;
using Constia.Application.Correo;
using Constia.Infrastructure.Correo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Constia.C3C02B.Tests;

public sealed class PureSafetyTests
{
    public static PreflightIdentity Preflight => new("isolation-only","ivandinicola/CONSTIA",new('a',40),new('f',64),
        new('b',40),SandboxIdentity.TrustedWorkflowRef,"1234","123","1",new('c',32),new('e',64),
        "c3c02b-"+new string('c',32),"10.8.0.0/24","10.8.0.1",new('1',64),"10.8.0.2",
        new('2',64),"10.8.0.3",new('3',64),"10.8.0.4","sha256:"+new string('d',64),
        SandboxIdentity.Sdk,"net:[123]",null,"prepared",40000);
    [Fact,Trait("Category","Pure")] public void IsolationRejectsAbsentUnknownAndFullPhases()
    {
        foreach(var phase in new string?[]{null,"","unknown","operational-full"})
            Assert.Throws<InvalidOperationException>(()=>PreflightIdentity.RequirePhase(phase));
        Assert.Throws<InvalidOperationException>(()=>Constia.C3C02B.Harness.Program.BuildIsolation("isolation-only",worker:true));
        Assert.Throws<InvalidOperationException>(()=>Constia.C3C02B.Harness.Program.BuildIsolation("isolation-only","real"));
    }
    [Fact,Trait("Category","Pure")] public void IsolationCompositionHasNoSqlWorkerCaptureOrRepository()
    {
        using var host=Constia.C3C02B.Harness.Program.BuildIsolation("isolation-only");
        Assert.Empty(host.Services.GetServices<IHostedService>());
        Assert.Null(host.Services.GetService<Constia.Infrastructure.ConstiaDbContext>());
        Assert.Null(host.Services.GetService<IOutboxBienvenidaRepository>());
        Assert.Null(host.Services.GetService<ProcesarBienvenida>());
        Assert.Null(host.Services.GetService<OpcionesProcesamientoBienvenida>());
        var factory=host.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        foreach(var name in new[]{"CONSTIA.Resend.C1","CONSTIA.Resend.C3","unregistered-client"})
        {
            var handler=factory.CreateHandler(name);
            while(handler is DelegatingHandler wrapper)handler=wrapper.InnerHandler!;
            Assert.IsType<SimulatedResendHandler>(handler);
        }
    }
    [Fact,Trait("Category","Pure")] public async Task IsolationBothNamedClientsUseSyntheticTransportOnly()
    {
        await Constia.C3C02B.Harness.Program.VerifyIsolationTransport();
    }
    [Fact,Trait("Category","Pure")] public void PreflightRejectsSqlFieldsWrongStageAndAmbiguousReceivers()
    {
        Preflight.Validate();
        var options=new System.Text.Json.JsonSerializerOptions{PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.SnakeCaseLower};
        var json=System.Text.Json.JsonSerializer.Serialize(Preflight,options);
        Assert.Equal(Preflight,PreflightIdentity.Parse(json));
        foreach(var field in new[]{"database","sql_major","runtime","connection_string"})
            Assert.Throws<System.Text.Json.JsonException>(()=>PreflightIdentity.Parse(json[..^1]+",\""+field+"\":\"synthetic\"}"));
        foreach(var bad in new[]{Preflight with{Phase="operational-full"},Preflight with{DeniedIp=Preflight.PeerIp},
            Preflight with{DeniedId=Preflight.PeerId},Preflight with{PeerIp="8.8.8.8"},Preflight with{ProofStage="isolated"},
            Preflight with{RulesSha256=new('a',64)},Preflight with{GatewayPort=1433}})
            Assert.Throws<InvalidOperationException>(()=>bad.Validate());
    }
    [Fact,Trait("Category","Pure")] public void PreflightBindsCodePolicyActorLauncherAndPhase()
    {
        var m=Preflight;
        var e=new Dictionary<string,string?>{
            ["GITHUB_ACTIONS"]="true",["RUNNER_ENVIRONMENT"]="github-hosted",["GITHUB_EVENT_NAME"]="workflow_dispatch",
            ["GITHUB_RUN_ATTEMPT"]="1",["GITHUB_REF"]="refs/heads/main",["GITHUB_WORKFLOW_REF"]=m.WorkflowRef,
            ["GITHUB_SHA"]=m.WorkflowSha,["GITHUB_WORKFLOW_SHA"]=m.WorkflowSha,["CODE_SHA"]=m.CodeSha,
            ["CONSTIA_B3_APPROVED_SHA"]=m.CodeSha,["CONSTIA_B3_INVENTORY_SHA256"]=m.PolicySha256,
            ["GITHUB_ACTOR_ID"]=m.ActorId,["CONSTIA_B3_APPROVED_ACTOR_ID"]=m.ActorId,["GITHUB_RUN_ID"]=m.RunId,
            ["C3C02B_PHASE"]="isolation-only",["CONSTIA_B3_APPROVED_PHASE"]="isolation-only"};
        m.VerifyEnvironment(k=>e.GetValueOrDefault(k),m.CodeSha);
        foreach(var key in new[]{"GITHUB_REF","GITHUB_ACTOR_ID","CODE_SHA","CONSTIA_B3_INVENTORY_SHA256","GITHUB_WORKFLOW_SHA","C3C02B_PHASE"})
        {
            var copy=new Dictionary<string,string?>(e){[key]="different"};
            Assert.Throws<InvalidOperationException>(()=>m.VerifyEnvironment(k=>copy.GetValueOrDefault(k),m.CodeSha));
        }
        Assert.Throws<InvalidOperationException>(()=>m.VerifyEnvironment(k=>e.GetValueOrDefault(k),new('c',40)));
    }
    [Fact,Trait("Category","Pure")] public async Task ExecutableRejectsWorkerAndSqlCommandsBeforeEnvironmentOrIo()
    {
        foreach(var args in new[]{Array.Empty<string>(),new[]{"worker","send","none"},new[]{"verify"},new[]{"operational-full"}})
            Assert.Equal(1,await Constia.C3C02B.Harness.Program.Main(args));
    }
    public static SandboxIdentity Synthetic => new("ivandinicola/CONSTIA",new('a',40),new('b',40),"123","1",
        new('c',32),new('d',64),"c3c02b-"+new string('c',32),SandboxIdentity.SqlImage,
        "C3C02B_"+new string('c',32),"172.29.0.2",new('d',12),new('e',64),
        "c3c02b-"+new string('c',32),SandboxIdentity.Sdk,"c3_runtime","c3_observer","c3_migrator",
        "172.29.0.0/24","172.29.0.1",SandboxIdentity.TrustedWorkflowRef);
    [Fact,Trait("Category","Pure")] public void PrivateTopologyUsesOwnedSubnetRatherThan172Assumption()
    {
        foreach(var (subnet,gateway,host) in new[]{("10.8.0.0/24","10.8.0.1","10.8.0.2"),
            ("192.168.60.0/24","192.168.60.1","192.168.60.2"),("172.29.0.0/24","172.29.0.1","172.29.0.2")})
            (Synthetic with{Subnet=subnet,Gateway=gateway,SqlIp=host}).Validate();
        foreach(var bad in new[]{Synthetic with{Gateway="10.8.0.1"},Synthetic with{SqlIp="172.29.0.255"},
            Synthetic with{SqlIp="172.29.0.1"},Synthetic with{Subnet="8.8.8.0/24",Gateway="8.8.8.1",SqlIp="8.8.8.2"},
            Synthetic with{Subnet="172.0.0.0/8"}})
            Assert.Throws<InvalidOperationException>(()=>bad.Validate());
    }
    [Fact,Trait("Category","Pure")] public void BothNamedClientsTerminateInSimulatedHandlers()
    {
        using var host=Constia.C3C02B.Harness.Program.Build(Synthetic,"defaults",new());
        var factory=host.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        foreach(var name in new[]{"CONSTIA.Resend.C1","CONSTIA.Resend.C3"}) {
            var handler=factory.CreateHandler(name);
            while(handler is DelegatingHandler delegated)handler=delegated.InnerHandler!;
            Assert.IsType<SimulatedResendHandler>(handler);
        }
    }
    [Fact,Trait("Category","Pure")] public void ForeignDatabaseAndImageAreRejected()
    {
        Synthetic.Validate();
        Assert.Throws<InvalidOperationException>(()=>(Synthetic with{Database="master"}).Validate());
        Assert.Throws<InvalidOperationException>(()=>(Synthetic with{SqlIp="127.0.0.1"}).Validate());
        Assert.Throws<InvalidOperationException>(()=>(Synthetic with{Image="sql:latest"}).Validate());
    }
    [Fact,Trait("Category","Pure")] public void NonManualIdentityIsRejected()
    {
        Assert.Throws<InvalidOperationException>(()=>(Synthetic with{Attempt="2"}).Validate());
        Assert.Throws<InvalidOperationException>(()=>(Synthetic with{RunId="0"}).Validate());
    }
    [Fact,Trait("Category","Pure")] public void DefaultsAndPartialConfigurationKeepAdmissionClosed()
    {
        using var host=Constia.C3C02B.Harness.Program.Build(Synthetic,"defaults",new());
        Assert.Empty(host.Services.GetServices<IHostedService>());
        Assert.False(host.Services.GetRequiredService<OpcionesProcesamientoBienvenida>().EnvioHabilitado);
        using var partial=Constia.C3C02B.Harness.Program.Build(Synthetic,"partial",new());
        Assert.False(partial.Services.GetRequiredService<OpcionesProcesamientoBienvenida>().EsValida);
        foreach(var flags in new[]{("false","true","synthetic-account"),("true","false","synthetic-account"),
            ("true","true",(string?)null),("invalid","true","synthetic-account"),("true","invalid","synthetic-account")}) {
            var o=OpcionesProcesamientoBienvenida.DesdeValores(flags.Item1,flags.Item2,"false",flags.Item3,"correo@example.invalid");
            Assert.False(o.EsValida && o.ProcesamientoHabilitado && o.EnvioHabilitado && o.Perfil is not null);
        }
        var positive=OpcionesProcesamientoBienvenida.DesdeValores("true","true","false","synthetic-account","correo@example.invalid");
        Assert.True(positive.EsValida && positive.ProcesamientoHabilitado && positive.EnvioHabilitado);
    }
    [Fact,Trait("Category","Pure")] public async Task FakeTransportHasNoNetworkFallback()
    {
        using var fake=new SimulatedResendHandler(new());
        using var client=new HttpClient(fake);
        var payload=Encoding.UTF8.GetBytes("""{"from":"CONSTIA <correo@example.invalid>","to":["synthetic@example.invalid"],"subject":"synthetic","html":"synthetic","text":"synthetic"}""");
        var transport=new ResendHttpTransport(client,new(){Enabled=true,ApiKey=SimulatedResendHandler.Key,
            FromAddress="correo@example.invalid",FromName="CONSTIA",RequestTimeoutSeconds=10},TimeProvider.System);
        var result=await transport.EnviarAsync(new("synthetic-key",payload),default);
        Assert.Equal(EstadoEntregaCorreo.AceptadaPorProveedor,result.Estado);
        Assert.Equal(1,fake.Calls);
        var bad=Encoding.UTF8.GetBytes("""{"from":"CONSTIA <correo@example.invalid>","to":["outside@unapproved.com"]}""");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>transport.EnviarAsync(new("synthetic-key",bad),default));
        Assert.Equal(1,fake.Calls);
    }
}

[Collection("Sandbox")]
public sealed class SqlSafetyTests
{
    [Fact,Trait("Category","Operational")] public async Task RestrictedRolesDenyDdlAndSensitiveColumns()
    {
        await using var f=new DisposableSqlDatabase(); await f.Initialize(); await f.Seed();
        foreach(var sql in new[]{"SELECT Destinatario FROM dbo.EventoBienvenida","SELECT PayloadUtf8 FROM dbo.EventoBienvenida",
            "SELECT PayloadSha256 FROM dbo.EventoBienvenida","SELECT IdProveedor FROM dbo.EventoBienvenida","SELECT Email FROM dbo.Usuario",
            "UPDATE dbo.ControlBienvenida SET EnvioHabilitado=1 WHERE Id=1"})
        {
            await using var c=f.Identity.Connection("observer",f.Identity.Database);
            await c.OpenAsync(); await f.Identity.Verify(c,f.Identity.Database);
            using var cmd=c.CreateCommand();cmd.CommandText=sql;
            var ex=await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>cmd.ExecuteNonQueryAsync());
            Assert.Equal(229,ex.Number);
        }
        await using var runtime=f.Identity.Connection("runtime",f.Identity.Database);
        await runtime.OpenAsync();await f.Identity.Verify(runtime,f.Identity.Database);
        using var ddl=runtime.CreateCommand();ddl.CommandText="CREATE TABLE dbo.Forbidden(Id int)";
        var denied=await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>ddl.ExecuteNonQueryAsync());
        Assert.Contains(denied.Number,new[]{229,262});
    }
}
