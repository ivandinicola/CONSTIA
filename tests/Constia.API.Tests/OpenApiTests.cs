using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Constia.Application.Cumplimientos;
using Constia.Application.Habitos;
using Constia.Application.Usuarios;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi;

namespace Constia.API.Tests;

public sealed class OpenApiTests
{
    private static readonly string[] Anonymous =
        ["post /api/auth/login", "post /api/usuarios", "post /api/auth/email/verificar", "post /api/auth/email/reenviar"];

    [Fact]
    public async Task Development_PublicaLas23OperacionesReales_SinReferenciasRotas()
    {
        using var factory = new JwtApiFactory();
        using var host = Development(factory);
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        if (Environment.GetEnvironmentVariable("CONSTIA_OPENAPI_EVIDENCE") is { } evidence)
            await File.WriteAllTextAsync(evidence, json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("3.1.1", root.GetProperty("openapi").GetString());
        var operations = Operations(root).ToArray();
        Assert.Equal(23, operations.Length);
        var descriptions = host.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups.Items.SelectMany(group => group.Items);
        Assert.Equal(descriptions.Select(api => $"{api.HttpMethod!.ToLowerInvariant()} /{api.RelativePath}").Order(),
            operations.Select(operation => operation.Key).Order());
        foreach (var (_, operation) in operations)
        {
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("description").GetString()));
            var error = operation.GetProperty("responses").GetProperty("500");
            Assert.True(error.GetProperty("content").TryGetProperty("application/problem+json", out _));
        }
        var parsed = OpenApiDocument.Parse(json, "json");
        Assert.NotNull(parsed.Diagnostic);
        Assert.Empty(parsed.Diagnostic.Errors);
        ValidateReferences(root, root);
        Assert.DoesNotContain("/__tests", json);
    }

    [Fact]
    public async Task Seguridad_19BearerYCuatroAnonimas_SinSeguridadGlobal()
    {
        using var factory = new JwtApiFactory();
        using var host = Development(factory);
        using var client = host.CreateClient();
        using var document = await Document(client);
        var root = document.RootElement;
        Assert.False(root.TryGetProperty("security", out _));
        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        var protectedCount = 0;
        foreach (var (key, operation) in Operations(root))
        {
            if (Anonymous.Contains(key))
                Assert.False(operation.TryGetProperty("security", out var security) && security.GetArrayLength() > 0);
            else
            {
                var security = Assert.Single(operation.GetProperty("security").EnumerateArray());
                Assert.Empty(security.GetProperty("Bearer").EnumerateArray());
                protectedCount++;
            }
        }
        Assert.Equal(19, protectedCount);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task FueraDeDevelopment_NoDocumentoNiUiNiAssets_InclusoAutenticado(string environment)
    {
        using var factory = new JwtApiFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = host.CreateClient();
        Authenticate(client, host, factory);
        foreach (var path in new[] { "/openapi/v1.json", "/swagger/index.html", "/swagger/swagger-ui-bundle.js", "/swagger/swagger-ui.css" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var api = await client.GetAsync("/api/habitos/iconos");
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
    }

    [Fact]
    public async Task Documento_NoResuelveRepositoriosNiEjecutaNegocio()
    {
        using var factory = new JwtApiFactory();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHabitoRepository>();
                services.AddScoped<IHabitoRepository>(_ => throw new InvalidOperationException("No se debe resolver el repositorio al documentar."));
                services.RemoveAll<ICategoriaHabitoReadRepository>();
                services.AddScoped<ICategoriaHabitoReadRepository>(_ => throw new InvalidOperationException("No consultar catálogos al documentar."));
                services.RemoveAll<ICumplimientoRepository>();
                services.AddScoped<ICumplimientoRepository>(_ => throw new InvalidOperationException("No consultar registros al documentar."));
            });
        });
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.CantidadDeCambiosGuardados);
        Assert.Equal(0, factory.CantidadDeHabitosGuardados);
        Assert.Equal(0, factory.CantidadDeCumplimientosGuardados);
    }

    [Fact]
    public async Task Esquemas_ConservanValidaciones_Omision_Legados_YTiposReales()
    {
        using var factory = new JwtApiFactory();
        using var host = Development(factory);
        using var client = host.CreateClient();
        using var document = await Document(client);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var create = schemas.GetProperty("CrearHabitoHttpRequest");
        Assert.Contains("categoriaId", Strings(create.GetProperty("required")));
        Assert.Contains("iconoId", Strings(create.GetProperty("required")));
        var createFields = create.GetProperty("properties");
        Assert.Equal("string", createFields.GetProperty("categoriaId").GetProperty("type").GetString());
        Assert.Equal(28, createFields.GetProperty("iconoId").GetProperty("enum").GetArrayLength());
        Assert.Equal(1, createFields.GetProperty("diasProgramados").GetProperty("minItems").GetInt32());
        Assert.False(createFields.TryGetProperty("propiedadesAdicionales", out _));
        Assert.False(createFields.TryGetProperty("fechaInicio", out _));
        Assert.Contains("fechaInicio", create.GetProperty("description").GetString());
        var edit = schemas.GetProperty("EditarHabitoHttpRequest");
        Assert.DoesNotContain("iconoId", Strings(edit.GetProperty("required")));
        Assert.DoesNotContain("categoriaId", Strings(edit.GetProperty("required")));
        Assert.Equal("string", edit.GetProperty("properties").GetProperty("iconoId").GetProperty("type").GetString());
        Assert.False(edit.GetProperty("properties").TryGetProperty("iconoIdInformado", out _));
        Assert.Equal(Enumerable.Range(0, 7), schemas.GetProperty("DayOfWeek").GetProperty("enum").EnumerateArray().Select(value => value.GetInt32()));
        Assert.Equal(new[] { 0, 1 }, schemas.GetProperty("EstadoHabito").GetProperty("enum").EnumerateArray().Select(value => value.GetInt32()));
        Assert.Equal("integer", schemas.GetProperty("DiaCalendarioResponse").GetProperty("properties").GetProperty("programados").GetProperty("type").GetString());
        var habit = schemas.GetProperty("HabitoResponse").GetProperty("properties");
        Assert.Contains("null", Strings(habit.GetProperty("iconoId").GetProperty("type")));
        Assert.Equal("date", habit.GetProperty("startDate").GetProperty("format").GetString());
        Assert.Equal(new[] { "cumplido", "pendiente", "no_programado", "desconocido" },
            Strings(schemas.GetProperty("DiaHistorialHabito").GetProperty("properties").GetProperty("estado").GetProperty("enum")));
        Assert.Contains("independiente", schemas.GetProperty("DiaHistorialHabito").GetProperty("properties").GetProperty("tieneRegistro").GetProperty("description").GetString());
        Assert.Equal(new[] { "disponible", "no_aplicable", "no_disponible" },
            Strings(schemas.GetProperty("MetricaRachaResponse").GetProperty("properties").GetProperty("estado").GetProperty("enum")));
        foreach (var name in new[] { "LoginUsuarioRequest", "RegistroUsuarioRequest", "ConfirmarCorreoRequest" })
        {
            var password = schemas.GetProperty(name).GetProperty("properties").GetProperty("password");
            Assert.True(password.GetProperty("writeOnly").GetBoolean());
            Assert.Equal("password", password.GetProperty("format").GetString());
            Assert.False(password.TryGetProperty("example", out _));
            Assert.False(password.TryGetProperty("default", out _));
        }
        Assert.False(schemas.GetProperty("LoginUsuarioRequest").GetProperty("properties").GetProperty("password").TryGetProperty("minLength", out _));
    }

    [Fact]
    public async Task Respuestas_Concurrencia_Rango_ErroresYDisponibilidad_NoSeUniforman()
    {
        using var factory = new JwtApiFactory();
        using var host = Development(factory);
        using var client = host.CreateClient();
        using var document = await Document(client);
        var root = document.RootElement;
        var detail = Operation(root, "get", "/api/habitos/{id}");
        Assert.True(detail.GetProperty("responses").GetProperty("200").GetProperty("headers").TryGetProperty("ETag", out _));
        var edit = Operation(root, "put", "/api/habitos/{id}");
        var precondition = Assert.Single(edit.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "If-Match");
        Assert.False(precondition.TryGetProperty("required", out var required) && required.GetBoolean());
        Assert.Equal("header", precondition.GetProperty("in").GetString());
        Assert.True(edit.GetProperty("responses").TryGetProperty("412", out _));
        Assert.False(edit.GetProperty("responses").GetProperty("200").TryGetProperty("headers", out var headers) && headers.TryGetProperty("ETag", out _));
        var range = Operation(root, "get", "/api/habitos/calendario");
        Assert.All(range.GetProperty("parameters").EnumerateArray(), p => Assert.True(p.GetProperty("required").GetBoolean()));
        Assert.Contains("rango completo", range.GetProperty("responses").GetProperty("422").GetProperty("description").GetString());
        var history = Operation(root, "get", "/api/habitos/{id}/historial").GetProperty("responses");
        Assert.False(history.TryGetProperty("422", out _));
        Assert.False(Operation(root, "get", "/api/habitos/{id}/cumplimientos").GetProperty("responses").TryGetProperty("503", out _));
        Assert.True(Operation(root, "get", "/api/usuarios/mi-fecha-local").GetProperty("responses").GetProperty("200").GetProperty("headers").TryGetProperty("Cache-Control", out _));
        var register = Operation(root, "post", "/api/usuarios").GetProperty("responses");
        Assert.Contains("registration_unavailable", register.GetProperty("503").GetProperty("description").GetString());
        Assert.True(register.GetProperty("429").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.Contains("habit_status_history_not_initialized", history.GetProperty("503").GetProperty("description").GetString());
        var resend = Operation(root, "post", "/api/auth/email/reenviar").GetProperty("responses").GetProperty("202");
        Assert.True(resend.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("properties").TryGetProperty("mensaje", out _));
        var login = Operation(root, "post", "/api/auth/login").GetProperty("responses");
        Assert.False(login.GetProperty("400").GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").TryGetProperty("anyOf", out _));
        var safe = login.GetProperty("500").GetProperty("content").GetProperty("application/problem+json").GetProperty("schema");
        Assert.Equal("internal_error", safe.GetProperty("properties").GetProperty("code").GetProperty("enum")[0].GetString());
        Assert.Contains("traceId", Strings(safe.GetProperty("required")));
        Assert.True(edit.GetProperty("responses").GetProperty("500").GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").TryGetProperty("anyOf", out _));
        foreach (var (_, operation) in Operations(root))
            foreach (var response in operation.GetProperty("responses").EnumerateObject())
                if (response.Name == "204") Assert.False(response.Value.TryGetProperty("content", out _));
    }

    [Fact]
    public async Task Ui_AssetsLocales_GetOnly_SinPersistenciaNiValidadorExterno()
    {
        using var factory = new JwtApiFactory();
        using var host = Development(factory);
        using var client = host.CreateClient();
        var html = await client.GetStringAsync("/swagger/index.html");
        Assert.Contains("swagger-ui-bundle.js", html);
        var initializer = await client.GetStringAsync("/swagger/index.js");
        Assert.Contains("\"supportedSubmitMethods\":[\"get\"]", initializer);
        Assert.Contains("\"persistAuthorization\":false", initializer);
        Assert.Contains("\"queryConfigEnabled\":false", initializer);
        Assert.Contains("\"validatorUrl\":null", initializer);
        Assert.DoesNotContain("preauthorize", initializer, StringComparison.OrdinalIgnoreCase);
        foreach (var path in new[] { "swagger-ui.css", "swagger-ui-bundle.js", "swagger-ui-standalone-preset.js" })
        {
            using var response = await client.GetAsync("/swagger/" + path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Content.Headers.ContentLength > 0);
        }
    }

    [Fact]
    public async Task GetProtegido_SinBearer401_ConJwtSintetico200_MismoHostDocumentado()
    {
        using var factory = new JwtApiFactory();
        using var host = Development(factory);
        using var client = host.CreateClient();
        using var challenge = await client.GetAsync("/api/habitos/iconos");
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);
        Assert.Equal("Bearer", Assert.Single(challenge.Headers.WwwAuthenticate).Scheme);
        Authenticate(client, host, factory);
        using var allowed = await client.GetAsync("/api/habitos/iconos");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var icons = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync());
        Assert.Equal(28, icons.RootElement.GetArrayLength());
        Assert.Equal("libro", icons.RootElement[0].GetProperty("id").GetString());
        Assert.Equal("bicicleta", icons.RootElement[27].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Errores_DocumentanSoloLasVariantesDeCadaOperacion()
    {
        using var factory = new JwtApiFactory();
        using var host = Development(factory);
        using var client = host.CreateClient();
        using var document = await Document(client);
        var root = document.RootElement;
        foreach (var path in new[] { "/api/auth/login", "/api/auth/email/reenviar" })
        {
            var response = Operation(root, "post", path).GetProperty("responses").GetProperty("400");
            var schema = response.GetProperty("content").GetProperty("application/problem+json").GetProperty("schema");
            Assert.False(schema.TryGetProperty("anyOf", out _));
            Assert.Contains("errors", schema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        }
        foreach (var path in new[] { "/api/habitos/diarios", "/api/habitos/calendario", "/api/habitos/{id}/historial" })
        {
            var schema = Operation(root, "get", path).GetProperty("responses").GetProperty("400").GetProperty("content").GetProperty("application/problem+json").GetProperty("schema");
            Assert.False(schema.TryGetProperty("anyOf", out _));
            if (schema.TryGetProperty("$ref", out var reference))
                schema = root.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1]);
            Assert.False(schema.GetProperty("properties").TryGetProperty("errors", out _));
        }
        foreach (var path in new[] { "/api/habitos/iconos", "/api/habitos/categorias" })
            Assert.False(Operation(root, "get", path).GetProperty("responses").GetProperty("401").TryGetProperty("content", out _));
        var confirm = Operation(root, "post", "/api/auth/email/verificar").GetProperty("responses").GetProperty("400");
        var alternatives = confirm.GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").GetProperty("anyOf");
        Assert.Equal(2, alternatives.GetArrayLength());
    }

    [Fact]
    public async Task CamposObligatorios_Desafio_Errores_YStatusSoloNumerico()
    {
        using var factory = new JwtApiFactory();
        using var host = Development(factory);
        using var client = host.CreateClient();
        using var document = await Document(client);
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");
        Assert.Contains("desafioId", Strings(schemas.GetProperty("ConfirmarCorreoRequest").GetProperty("required")));
        var validation = Operation(root, "post", "/api/auth/login").GetProperty("responses").GetProperty("400").GetProperty("content").GetProperty("application/problem+json").GetProperty("schema");
        Assert.Contains("errors", Strings(validation.GetProperty("required")));
        Assert.Equal(new[] { "null", "integer" }, Strings(validation.GetProperty("properties").GetProperty("status").GetProperty("type")));
        Assert.Equal(new[] { "null", "integer" }, Strings(schemas.GetProperty("ProblemDetails").GetProperty("properties").GetProperty("status").GetProperty("type")));
    }

    [Fact]
    public async Task ServidorRelativo_ExitosJson_YEjemplosSinteticos()
    {
        using var factory = new JwtApiFactory();
        using var host = Development(factory);
        using var client = host.CreateClient();
        using var document = await Document(client);
        var root = document.RootElement;
        Assert.Equal("/", Assert.Single(root.GetProperty("servers").EnumerateArray()).GetProperty("url").GetString());
        foreach (var (_, operation) in Operations(root))
            foreach (var response in operation.GetProperty("responses").EnumerateObject().Where(r => r.Name.StartsWith('2') && r.Name != "204"))
                Assert.Equal(new[] { "application/json" }, response.Value.GetProperty("content").EnumerateObject().Select(p => p.Name));
        var partial = Operation(root, "get", "/api/habitos/rachas").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("example");
        Assert.Equal("no_disponible", partial.GetProperty("mejor").GetProperty("estado").GetString());
        var legacy = Operation(root, "get", "/api/habitos/{id}").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("example");
        Assert.Equal(JsonValueKind.Null, legacy.GetProperty("iconoId").ValueKind);
        Assert.Equal(JsonValueKind.Null, legacy.GetProperty("categoriaId").ValueKind);
    }
    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Development(JwtApiFactory factory) =>
        factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    private static async Task<JsonDocument> Document(HttpClient client) => JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
    private static JsonElement Operation(JsonElement root, string method, string path) => root.GetProperty("paths").GetProperty(path).GetProperty(method);
    private static IEnumerable<(string Key, JsonElement Value)> Operations(JsonElement root) => root.GetProperty("paths").EnumerateObject()
        .SelectMany(path => path.Value.EnumerateObject().Select(operation => ($"{operation.Name} {path.Name}", operation.Value)));
    private static string?[] Strings(JsonElement array) => array.EnumerateArray().Select(value => value.GetString()).ToArray();
    private static void Authenticate(HttpClient client, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, JwtApiFactory factory)
    {
        using var scope = host.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IAccessTokenService>().Emitir(factory.Usuario.Id, factory.Usuario.Nombre, factory.Usuario.Email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }
    private static void ValidateReferences(JsonElement node, JsonElement root)
    {
        if (node.ValueKind == JsonValueKind.Object)
            foreach (var field in node.EnumerateObject())
            {
                if (field.Name == "$ref")
                {
                    var reference = field.Value.GetString()!;
                    Assert.StartsWith("#/", reference);
                    var target = root;
                    foreach (var part in reference[2..].Split('/'))
                        Assert.True(target.TryGetProperty(part.Replace("~1", "/").Replace("~0", "~"), out target), reference);
                }
                else ValidateReferences(field.Value, root);
            }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) ValidateReferences(item, root);
    }
}
