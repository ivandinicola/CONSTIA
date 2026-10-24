using System.Data.Common;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Constia.C3C02B.Harness;

// This identity contains no database, role, SQL version or connection credential.
public sealed record PreflightIdentity(string Phase,string Repository,string CodeSha,string PolicySha256,
    string WorkflowSha,string WorkflowRef,string ActorId,string RunId,string Attempt,string Nonce,
    string NetworkId,string NetworkName,string Subnet,string Gateway,string AppId,string AppIp,
    string PeerId,string PeerIp,string DeniedId,string DeniedIp,string ImageId,string SdkImage,
    string Namespace,string? RulesSha256,string ProofStage,int GatewayPort)
{
    public const string Isolation="isolation-only";
    public static void RequirePhase(string? phase)
    {
        if(phase!=Isolation)throw new InvalidOperationException("Explicit isolation-only phase required; full is blocked.");
    }
    public void Validate()
    {
        RequirePhase(Phase);
        bool Hex(string? s,int n)=>s is not null && Regex.IsMatch(s,"^[0-9a-f]{"+n+"}$");
        bool Number(string? s)=>s is not null && Regex.IsMatch(s,"^[1-9][0-9]*$");
        if(Repository!="ivandinicola/CONSTIA" || WorkflowRef!=SandboxIdentity.TrustedWorkflowRef || Attempt!="1" ||
            !Number(ActorId) || !Number(RunId) || !Hex(CodeSha,40) || !Hex(WorkflowSha,40) || !Hex(PolicySha256,64) ||
            !Hex(Nonce,32) || !Hex(NetworkId,64) || !Hex(AppId,64) || !Hex(PeerId,64) || !Hex(DeniedId,64) ||
            new[]{AppId,PeerId,DeniedId}.Distinct().Count()!=3 || NetworkName!="c3c02b-"+Nonce ||
            SdkImage!=SandboxIdentity.Sdk || ImageId is null || !Regex.IsMatch(ImageId,"^sha256:[0-9a-f]{64}$") ||
            Namespace is null || !Regex.IsMatch(Namespace,@"^net:\[[0-9]+\]$") ||
            GatewayPort is <1024 or >65535 || GatewayPort==1433 ||
            new[]{AppIp,PeerIp,DeniedIp}.Distinct().Count()!=3 ||
            !new[]{AppIp,PeerIp,DeniedIp}.All(ip=>SandboxIdentity.ValidTopology(Subnet,Gateway,ip)) ||
            ProofStage is not ("prepared" or "isolated") ||
            (ProofStage=="prepared" && RulesSha256 is not null) || (ProofStage=="isolated" && !Hex(RulesSha256,64)))
            throw new InvalidOperationException("Preflight identity rejected.");
    }
    public static PreflightIdentity Parse(string json)
    {
        using var doc=JsonDocument.Parse(json);
        var names=doc.RootElement.EnumerateObject().Select(x=>x.Name).ToArray();
        if(names.Length!=names.Distinct(StringComparer.Ordinal).Count())throw new InvalidOperationException("Duplicate identity field.");
        var value=JsonSerializer.Deserialize<PreflightIdentity>(json,new JsonSerializerOptions{
            PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
            UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow})
            ?? throw new InvalidOperationException("Missing preflight identity.");
        value.Validate();return value;
    }
    public void VerifyEnvironment(Func<string,string?> get,string revision)
    {
        Validate();RequirePhase(get("C3C02B_PHASE"));RequirePhase(get("CONSTIA_B3_APPROVED_PHASE"));
        if(get("GITHUB_ACTIONS")!="true" || get("RUNNER_ENVIRONMENT")!="github-hosted" ||
            get("GITHUB_EVENT_NAME")!="workflow_dispatch" || get("GITHUB_RUN_ATTEMPT")!="1" ||
            get("GITHUB_REF")!="refs/heads/main" || get("GITHUB_WORKFLOW_REF")!=WorkflowRef ||
            get("GITHUB_SHA")!=WorkflowSha || get("GITHUB_WORKFLOW_SHA")!=WorkflowSha ||
            get("CODE_SHA")!=CodeSha || get("CONSTIA_B3_APPROVED_SHA")!=CodeSha || revision!=CodeSha ||
            get("CONSTIA_B3_INVENTORY_SHA256")!=PolicySha256 || get("GITHUB_ACTOR_ID")!=ActorId ||
            get("CONSTIA_B3_APPROVED_ACTOR_ID")!=ActorId || get("GITHUB_RUN_ID")!=RunId)
            throw new InvalidOperationException("Preflight authorization mismatch.");
    }
    public static PreflightIdentity Load(string role)
    {
        if(!OperatingSystem.IsLinux())throw new InvalidOperationException("Hosted Linux preflight required.");
        var identity=Parse(File.ReadAllText("/run/c3/preflight.json"));
        var revision=typeof(PreflightIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(x=>x.Key=="SandboxRevision")?.Value;
        identity.VerifyEnvironment(Environment.GetEnvironmentVariable,revision??"");
        var id=role switch{"app"=>identity.AppId,"peer"=>identity.PeerId,"denied"=>identity.DeniedId,
            _=>throw new InvalidOperationException("Unknown preflight role.")};
        if(Environment.MachineName!=id[..12])throw new InvalidOperationException("Container identity mismatch.");
        if(role=="app" && new FileInfo("/proc/self/ns/net").LinkTarget!=identity.Namespace)
            throw new InvalidOperationException("Network namespace mismatch.");
        return identity;
    }
}

public sealed record SandboxIdentity(string Repository, string CodeSha, string WorkflowSha, string RunId, string Attempt,
    string Nonce, string ContainerId, string ContainerName, string Image, string Database, string SqlIp, string Hostname,
    string NetworkId, string NetworkName, string SdkImage, string Runtime, string Observer, string Migrator,
    string Subnet,string Gateway,string WorkflowRef)
{
    public const string TrustedWorkflowRef="ivandinicola/CONSTIA/.github/workflows/c3c02b-operational.yml@refs/heads/main";
    public const string SqlImage = "mcr.microsoft.com/mssql/server@sha256:cf8f0ab2ebe61feec2eed2629061a07d247f791c2a8ddda74a4ac9f06a291718";
    public const string Sdk = "mcr.microsoft.com/dotnet/sdk@sha256:8dd1a57fe6b7b8b1b357635db67e272c2f9e9d76d1dd0a32711c4c33c57d7b43";
    public void Validate()
    {
        bool Hex(string s, int n) => Regex.IsMatch(s, "^[0-9a-f]{" + n + "}$");
        if (Repository != "ivandinicola/CONSTIA" || Attempt != "1" || !Regex.IsMatch(RunId,"^[1-9][0-9]*$") ||
            !Hex(CodeSha,40) || !Hex(WorkflowSha,40) || !Hex(Nonce,32) || !Hex(ContainerId,64) || !Hex(NetworkId,64) ||
            ContainerName != "c3c02b-"+Nonce || NetworkName != ContainerName || Database != "C3C02B_"+Nonce ||
            Hostname != ContainerId[..12] || Image != SqlImage || SdkImage != Sdk ||
            Runtime != "c3_runtime" || Observer != "c3_observer" || Migrator != "c3_migrator" ||
            WorkflowRef!=TrustedWorkflowRef || !ValidTopology(Subnet,Gateway,SqlIp))
            throw new InvalidOperationException("Sandbox identity rejected.");
    }
    public static bool ValidTopology(string subnet,string gateway,string host)
    {
        if(!IPNetwork.TryParse(subnet,out var net) || net.BaseAddress.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork ||
            net.ToString()!=subnet || net.PrefixLength>29 || !IPAddress.TryParse(gateway,out var gw) || !IPAddress.TryParse(host,out var ip) ||
            gw.ToString()!=gateway || ip.ToString()!=host || !net.Contains(gw) || !net.Contains(ip))return false;
        var ranges=new[]{IPNetwork.Parse("10.0.0.0/8"),IPNetwork.Parse("172.16.0.0/12"),IPNetwork.Parse("192.168.0.0/16")};
        if(!ranges.Any(x=>net.PrefixLength>=x.PrefixLength && x.Contains(net.BaseAddress)))return false;
        var first=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(net.BaseAddress.GetAddressBytes());
        var last=first | (uint.MaxValue >> net.PrefixLength);
        bool Host(IPAddress address) {
            if(address.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)return false;
            var value=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
            return value!=first && value!=last;
        }
        return Host(gw) && Host(ip) && !gw.Equals(ip);
    }
    public static SandboxIdentity Load()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" ||
            Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") != "workflow_dispatch" ||
            Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") != "1" ||
            Environment.GetEnvironmentVariable("GITHUB_REF")!="refs/heads/main" ||
            Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_REF")!=TrustedWorkflowRef ||
            Environment.GetEnvironmentVariable("GITHUB_SHA")!=Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_SHA"))
            throw new InvalidOperationException("Hosted manual sandbox required.");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
        var identity = JsonSerializer.Deserialize<SandboxIdentity>(File.ReadAllText("/run/c3/identity.json"),options)
            ?? throw new InvalidOperationException("No identity.");
        identity.Validate();
        var revision=typeof(SandboxIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(x=>x.Key=="SandboxRevision")?.Value;
        if(revision!=identity.CodeSha)
            throw new InvalidOperationException("Assembly revision is not the approved revision.");
        if (identity.CodeSha != Environment.GetEnvironmentVariable("CODE_SHA") ||
            identity.CodeSha != Environment.GetEnvironmentVariable("CONSTIA_B3_APPROVED_SHA") ||
            identity.WorkflowSha != Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_SHA") ||
            identity.WorkflowRef != Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_REF") ||
            identity.RunId != Environment.GetEnvironmentVariable("GITHUB_RUN_ID"))
            throw new InvalidOperationException("Revision/run mismatch.");
        return identity;
    }
    public SqlConnection Connection(string role, string database)
    {
        Validate();
        if (database != Database && database != "master") throw new InvalidOperationException("Foreign database.");
        if (role != "admin" && database == "master") throw new InvalidOperationException("Master requires bootstrap identity.");
        if (role is not ("admin" or "runtime" or "observer" or "migrator")) throw new InvalidOperationException("Unknown role.");
        var credentials = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText("/run/c3/credentials.json"))!;
        var password = credentials[role];
        if (!Regex.IsMatch(password, "^[0-9a-f]{64}aA!7$")) throw new InvalidOperationException("Ephemeral credential required.");
        return new SqlConnection(new SqlConnectionStringBuilder {
            DataSource = SqlIp+",1433", InitialCatalog=database, UserID=role=="admin"?"sa":"c3_"+role,
            Password=password, Encrypt=false, TrustServerCertificate=true, Pooling=false, ConnectTimeout=1
        }.ConnectionString);
    }
    public async Task Verify(SqlConnection connection, string database, CancellationToken ct=default)
    {
        Validate();
        if (database != Database && database != "master") throw new InvalidOperationException("Foreign database.");
        var b=new SqlConnectionStringBuilder(connection.ConnectionString);
        if (b.DataSource != SqlIp+",1433" || b.InitialCatalog != database || b.IntegratedSecurity)
            throw new InvalidOperationException("Connection target rejected.");
        using var cmd=connection.CreateCommand(); cmd.CommandTimeout=2;
        cmd.CommandText="SELECT CONVERT(int,SERVERPROPERTY('ProductMajorVersion')),CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),DB_NAME(),ORIGINAL_LOGIN();";
        using var reader=await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetInt32(0)!=16 || reader.GetString(1)!=Hostname ||
            reader.GetString(2)!=database || reader.GetString(3)!=b.UserID)
            throw new InvalidOperationException("Physical SQL identity rejected.");
    }
}

public sealed class IdentityConnectionInterceptor(SandboxIdentity identity) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection c, ConnectionEndEventData e) =>
        identity.Verify((SqlConnection)c,identity.Database).GetAwaiter().GetResult();
    public override Task ConnectionOpenedAsync(DbConnection c, ConnectionEndEventData e,CancellationToken ct=default) =>
        identity.Verify((SqlConnection)c,identity.Database,ct);
}
