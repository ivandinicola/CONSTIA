using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Constia.API.Tests;

// Identidad exclusiva de CAT-02, no compartida con las fixtures históricas.
internal sealed record Cat02SqlRunnerIdentity(
    string ContainerId, string ImageId, string ImageDigest, string Nonce,
    string Hostname, int Port, string RunId, string RunAttempt)
{
    internal void Validate(string runId, string attempt)
    {
        if (!Regex.IsMatch(ContainerId ?? "", "^[0-9a-f]{64}$")
            || !Regex.IsMatch(ImageId ?? "", "^sha256:[0-9a-f]{64}$")
            || !Regex.IsMatch(ImageDigest ?? "", "^mcr\\.microsoft\\.com/mssql/server@sha256:[0-9a-f]{64}$")
            || !Regex.IsMatch(Nonce ?? "", "^[0-9a-f]{32}$")
            || Hostname != $"c2-{Nonce![..12]}" || Port is < 1024 or > 65535 || Port == 1433
            || !Regex.IsMatch(RunId ?? "", "^[0-9]+$") || RunId != runId
            || !Regex.IsMatch(RunAttempt ?? "", "^[0-9]+$") || RunAttempt != attempt)
            throw new InvalidOperationException("Invalid CAT-02 execution manifest.");
    }

    internal void ValidateContainer(JsonElement container)
    {
        var config = container.GetProperty("Config");
        var host = container.GetProperty("HostConfig");
        var labels = config.GetProperty("Labels");
        if (container.GetProperty("Id").GetString() != ContainerId
            || container.GetProperty("Image").GetString() != ImageId
            || !container.GetProperty("State").GetProperty("Running").GetBoolean()
            || config.GetProperty("Hostname").GetString() != Hostname
            || config.GetProperty("Image").GetString() != ImageDigest
            || labels.GetProperty("com.constia.cat02.run").GetString() != Nonce
            || labels.GetProperty("com.constia.cat02.github-run").GetString() != RunId
            || labels.GetProperty("com.constia.cat02.attempt").GetString() != RunAttempt
            || labels.GetProperty("com.constia.cat02.scope").GetString() != "cat02-sqlserver"
            || container.GetProperty("Mounts").GetArrayLength() != 0
            || host.GetProperty("NetworkMode").GetString() != "bridge"
            || host.GetProperty("Privileged").GetBoolean()
            || !string.IsNullOrEmpty(host.GetProperty("PidMode").GetString())
            || HasItems(host.GetProperty("Binds")) || HasItems(host.GetProperty("VolumesFrom")))
            throw new InvalidOperationException("CAT-02 container identity/isolation mismatch.");
        ValidatePorts(host.GetProperty("PortBindings"), requested: true);
        ValidatePorts(container.GetProperty("NetworkSettings").GetProperty("Ports"));
    }

    private void ValidatePorts(JsonElement ports, bool requested = false)
    {
        if (ports.EnumerateObject().Count() != 1) throw new InvalidOperationException("Unexpected CAT-02 ports.");
        var bindings = ports.GetProperty("1433/tcp");
        if (bindings.GetArrayLength() != 1 || bindings[0].GetProperty("HostIp").GetString() != "127.0.0.1"
            || !(bindings[0].GetProperty("HostPort").GetString() == Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || (requested && bindings[0].GetProperty("HostPort").GetString() is "" or "0")))
            throw new InvalidOperationException("CAT-02 port mismatch.");
    }

    private static bool HasItems(JsonElement value) => value.ValueKind != JsonValueKind.Null && value.GetArrayLength() != 0;

    internal static bool IsEnabled() =>
        Environment.GetEnvironmentVariable("CONSTIA_CAT02_SQLSERVER_TESTS") == "true"
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CONSTIA_CAT02_MANIFEST"));

    internal static void RequireHostedRunner()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
            || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted"
            || Environment.GetEnvironmentVariable("GITHUB_JOB") != "cat02-sqlserver"
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_CONTEXT"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CONSTIA_CAT02_SQLSERVER_CONNECTION")))
            throw new InvalidOperationException("CAT-02 requires its dedicated hosted runner and cannot accept arbitrary connections.");
    }

    internal static Cat02SqlRunnerIdentity Read()
    {
        RequireHostedRunner();
        if (!IsEnabled()) throw new InvalidOperationException("CAT-02 is not enabled by its orchestrator.");
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? throw new InvalidOperationException("Missing runner temp."));
        var path = Path.GetFullPath(Environment.GetEnvironmentVariable("CONSTIA_CAT02_MANIFEST")!);
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("CAT-02 manifest is outside runner temp or is a link.");
        var identity = JsonSerializer.Deserialize<Cat02SqlRunnerIdentity>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Missing CAT-02 identity.");
        identity.Validate(Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "", Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") ?? "");
        return identity;
    }

    internal async Task<string> VerifyAdminAsync(string password)
    {
        RequireHostedRunner();
        Validate(Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "", Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") ?? "");
        var context = await DockerJsonAsync("context", "inspect");
        if (context.GetArrayLength() != 1 || context[0].GetProperty("Endpoints").GetProperty("docker").GetProperty("Host").GetString() != "unix:///var/run/docker.sock")
            throw new InvalidOperationException("CAT-02 requires the local runner Docker daemon.");
        var inspected = await DockerJsonAsync("inspect", ContainerId);
        if (inspected.GetArrayLength() != 1) throw new InvalidOperationException("Missing CAT-02 container.");
        ValidateContainer(inspected[0]);
        var image = await DockerJsonAsync("image", "inspect", ImageDigest);
        if (image.GetArrayLength() != 1 || image[0].GetProperty("Id").GetString() != ImageId
            || !image[0].GetProperty("RepoDigests").EnumerateArray().Any(d => d.GetString() == ImageDigest))
            throw new InvalidOperationException("CAT-02 image digest mismatch.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 32) throw new InvalidOperationException("Missing ephemeral CAT-02 credential.");
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:127.0.0.1,{Port}", InitialCatalog = "master", UserID = "sa", Password = password,
            Encrypt = true, TrustServerCertificate = true, ConnectTimeout = 5, Pooling = false,
            ApplicationName = $"CONSTIA-CAT02-{Nonce}"
        };
        // Solo lectura: ninguna base se crea hasta comprobar la identidad real.
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT CONVERT(nvarchar(128), SERVERPROPERTY('MachineName')), CONVERT(int, SERVERPROPERTY('ProductMajorVersion')), DB_NAME();", connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || !string.Equals(reader.GetString(0), Hostname, StringComparison.OrdinalIgnoreCase)
            || reader.GetInt32(1) != 16 || reader.GetString(2) != "master")
            throw new InvalidOperationException("CAT-02 SQL engine identity mismatch; no database may be created.");
        return builder.ConnectionString;
    }

    private static async Task<JsonElement> DockerJsonAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Docker inspection unavailable.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new InvalidOperationException("CAT-02 Docker inspection timed out."); }
        await error; // No volcar inspect ni errores: Config.Env puede contener credenciales.
        if (process.ExitCode != 0) throw new InvalidOperationException("CAT-02 Docker inspection failed.");
        using var json = JsonDocument.Parse(await output);
        return json.RootElement.Clone();
    }
}
