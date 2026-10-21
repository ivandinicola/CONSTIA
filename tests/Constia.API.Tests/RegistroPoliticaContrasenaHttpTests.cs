using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Constia.API.Tests;

public sealed class RegistroPoliticaContrasenaHttpTests(JwtApiFactory factory) : IClassFixture<JwtApiFactory>
{
    public static TheoryData<string> Invalidas => new()
    {
        "ABCD1.@",
        new string('A', 127) + "1.",
        "abcdef1.",
        "ABCDEFG.",
        "ABCDEFG1",
        "ABCDE1  ",
        "ABCD1.\U0001F600",
        "ABCDE\u00B2.@"
    };

    public static TheoryData<string> Validas => new()
    {
        "ABCDE1.@",
        new string('A', 126) + "1.",
        new string('A', 125) + "1.\U0001F600",
        "ABCDEFG1,",
        " ABCD1.@ "
    };

    [Theory]
    [MemberData(nameof(Invalidas))]
    public async Task Registro_PoliticaInvalida_Devuelve400ConPasswordYSinDatosSensibles(string password)
    {
        using var host = CrearHostConHasherReal();
        using var client = host.CreateClient();
        var email = $"{Guid.NewGuid()}@example.invalid";

        var response = await client.PostAsJsonAsync("/api/usuarios", new
        {
            nombre = "Ana", email, password, timeZoneId = "Etc/UTC"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.True(problem.Errors.TryGetValue("Password", out var errores));
        Assert.NotEmpty(errores);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(password, body);
        Assert.DoesNotContain("ContrasenaInvalidaException", body);
        Assert.DoesNotContain("PasswordHash", body);
        Assert.DoesNotContain(factory.UsuariosPersistidos.Values, usuario => usuario.Email == email);
    }

    [Theory]
    [MemberData(nameof(Validas))]
    public async Task Registro_PoliticaValida_Conserva201YContratoYHashSeguroDelValorExacto(string password)
    {
        using var host = CrearHostConHasherReal();
        using var client = host.CreateClient();
        var email = $"{Guid.NewGuid()}@example.invalid";

        var response = await client.PostAsJsonAsync("/api/usuarios", new
        {
            nombre = "Ana", email, password, timeZoneId = "Etc/UTC"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = json.RootElement;
        Assert.Equal(new[] { "email", "id", "nombre" }, body.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("Ana", body.GetProperty("nombre").GetString());
        Assert.Equal(email, body.GetProperty("email").GetString());
        var id = body.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        var usuario = factory.UsuariosPersistidos[id];
        Assert.NotEqual(password, usuario.PasswordHash);
        Assert.True(new AspNetCorePasswordHasher().VerifyPassword(usuario.PasswordHash, password));
    }

    [Fact]
    public async Task Registro_EmailDuplicadoConPasswordValida_Conserva409()
    {
        using var host = CrearHostConHasherReal();
        using var client = host.CreateClient();
        var original = factory.Usuario.PasswordHash;

        var response = await client.PostAsJsonAsync("/api/usuarios", new
        {
            nombre = "Duplicado", email = factory.Usuario.Email,
            password = "ABCDE1.@", timeZoneId = "Etc/UTC"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("El email ya está registrado.", problem.Detail);
        Assert.Equal(original, factory.Usuario.PasswordHash);
    }

    [Theory]
    [InlineData("vieja")]
    [InlineData(" clave historica ")]
    public async Task Login_HashHistoricoConPasswordFueraDeLaPolitica_Conserva200YJwt(string password)
    {
        using var host = CrearHostConHasherReal();
        using var client = host.CreateClient();
        var hash = new AspNetCorePasswordHasher().HashPassword(password);
        var usuario = new Usuario("Historico", $"{Guid.NewGuid()}@example.invalid", hash, "Etc/UTC");
        Assert.True(factory.UsuariosPersistidos.TryAdd(usuario.Id, usuario));

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = usuario.Email, password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("accessToken").GetString()));
        Assert.Equal(usuario.Id, json.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal(hash, factory.UsuariosPersistidos[usuario.Id].PasswordHash);
    }

    [Theory]
    [InlineData(@"\uD800")]
    [InlineData(@"\uDC00")]
    [InlineData(@"\uD800x")]
    public async Task Registro_JsonConUtf16Malformado_Rechaza400SinPersistencia(string sustitutoJson)
    {
        using var host = CrearHostConHasherReal();
        using var client = host.CreateClient();
        var email = $"{Guid.NewGuid()}@example.invalid";
        var json = $$"""{"nombre":"Ana","email":"{{email}}","password":"ABCDE1.{{sustitutoJson}}","timeZoneId":"Etc/UTC"}""";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/usuarios", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(factory.UsuariosPersistidos.Values, usuario => usuario.Email == email);
    }

    // Mantener reales politica, hasher, controlador y pipeline; sustituir solo la persistencia ya aislada.
    private WebApplicationFactory<Program> CrearHostConHasherReal() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUsuarioPasswordHasher>();
            services.AddScoped<IUsuarioPasswordHasher, AspNetCorePasswordHasher>();
        }));
}
