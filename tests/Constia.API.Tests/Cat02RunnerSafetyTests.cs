using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Constia.API.Tests;

[CollectionDefinition("Cat02RunnerEnvironment", DisableParallelization = true)]
public sealed class Cat02RunnerEnvironmentCollection;

[Collection("Cat02RunnerEnvironment")]
public sealed class Cat02RunnerSafetyTests
{
    [Fact]
    public void OptInSinManifiesto_NoHabilitaSqlAunqueSimuleGitHubActions()
    {
        var changes = new Dictionary<string, string?>
        {
            ["GITHUB_ACTIONS"] = "true",
            ["CONSTIA_CAT02_SQLSERVER_TESTS"] = "true",
            ["CONSTIA_CAT02_SQLSERVER_CONNECTION"] = "Server=127.0.0.1,1433;Database=master;User Id=sa;Password=not-a-real-secret",
            ["CONSTIA_CAT02_MANIFEST"] = null
        };
        var before = changes.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var pair in changes) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            var attributeType = typeof(CategoriaHabitoSqlServerTests)
                .GetNestedType("DisposableSqlFactAttribute", BindingFlags.NonPublic)!;
            var attribute = (FactAttribute)Activator.CreateInstance(attributeType)!;
            Assert.NotNull(attribute.Skip);
        }
        finally
        {
            foreach (var pair in before) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
    private static Cat02SqlRunnerIdentity ValidIdentity() => new(
        new string('a', 64), "sha256:" + new string('b', 64),
        "mcr.microsoft.com/mssql/server@sha256:" + new string('c', 64),
        new string('d', 32), "c2-" + new string('d', 12), 49152, "123", "1");

    [Fact]
    public void IdentidadValida_AceptaSoloSuContenedorAislado()
    {
        var identity = ValidIdentity();
        identity.Validate("123", "1");
        identity.ValidateContainer(JsonSerializer.SerializeToElement(Container(identity)));
    }

    [Theory]
    [InlineData("container")]
    [InlineData("digest")]
    [InlineData("nonce")]
    [InlineData("hostname")]
    [InlineData("port")]
    [InlineData("run")]
    [InlineData("attempt")]
    public void ManifiestoInvalido_SeRechazaSinDockerNiSql(string field)
    {
        var identity = ValidIdentity();
        identity = field switch
        {
            "container" => identity with { ContainerId = "" },
            "digest" => identity with { ImageDigest = "other/image@sha256:" + new string('c', 64) },
            "nonce" => identity with { Nonce = "" },
            "hostname" => identity with { Hostname = "development" },
            "port" => identity with { Port = 1433 },
            "run" => identity with { RunId = "999" },
            _ => identity with { RunAttempt = "2" }
        };
        Assert.Throws<InvalidOperationException>(() => identity.Validate("123", "1"));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("image")]
    [InlineData("stopped")]
    [InlineData("label")]
    [InlineData("mount")]
    [InlineData("network")]
    [InlineData("public-port")]
    [InlineData("other-port")]
    public void ContenedorAjenoOInseguro_SeRechazaSinDockerNiSql(string field)
    {
        var identity = ValidIdentity();
        var node = JsonSerializer.SerializeToNode(Container(identity))!;
        switch (field)
        {
            case "id": node["Id"] = new string('e', 64); break;
            case "image": node["Image"] = "sha256:" + new string('e', 64); break;
            case "stopped": node["State"]!["Running"] = false; break;
            case "label": node["Config"]!["Labels"]!["com.constia.cat02.run"] = "other"; break;
            case "mount": node["Mounts"] = new JsonArray(new JsonObject { ["Type"] = "bind" }); break;
            case "network": node["HostConfig"]!["NetworkMode"] = "host"; break;
            case "public-port": node["NetworkSettings"]!["Ports"]!["1433/tcp"]![0]!["HostIp"] = "0.0.0.0"; break;
            case "other-port": node["HostConfig"]!["PortBindings"]!["1433/tcp"]![0]!["HostPort"] = "1433"; break;
        }
        Assert.Throws<InvalidOperationException>(() => identity.ValidateContainer(JsonSerializer.SerializeToElement(node)));
    }

    private static object Container(Cat02SqlRunnerIdentity identity)
    {
        var ports = new Dictionary<string, object>
        {
            ["1433/tcp"] = new[] { new { HostIp = "127.0.0.1", HostPort = identity.Port.ToString() } }
        };
        return new
        {
            Id = identity.ContainerId, Image = identity.ImageId,
            State = new { Running = true }, Mounts = Array.Empty<object>(),
            Config = new { identity.Hostname, Image = identity.ImageDigest, Labels = new Dictionary<string, string>
            {
                ["com.constia.cat02.run"] = identity.Nonce,
                ["com.constia.cat02.github-run"] = identity.RunId,
                ["com.constia.cat02.attempt"] = identity.RunAttempt,
                ["com.constia.cat02.scope"] = "cat02-sqlserver"
            } },
            HostConfig = new { NetworkMode = "bridge", Privileged = false, PidMode = "", Binds = (string[]?)null,
                VolumesFrom = (string[]?)null, PortBindings = new Dictionary<string, object>
                { ["1433/tcp"] = new[] { new { HostIp = "127.0.0.1", HostPort = "" } } } },
            NetworkSettings = new { Ports = ports }
        };
    }
}