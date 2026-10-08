using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Constia.API.Contracts;
using Constia.Domain;
using Microsoft.IdentityModel.Tokens;

namespace Constia.API.Tests;

public sealed class ZonaHorariaUsuarioTests(JwtApiFactory factory) : IClassFixture<JwtApiFactory>
{
    [Fact]
    public async Task Registro_ConZonaIanaValida_PersisteLaZonaElegida()
    {
        using var client = factory.CreateClient();
        var email = $"{Guid.NewGuid()}@example.invalid";

        var response = await client.PostAsJsonAsync("/api/usuarios", new
        {
            nombre = "Nueva persona",
            email,
            password = "fictitious-registration-password",
            timeZoneId = "America/Argentina/Buenos_Aires"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains(factory.UsuariosPersistidos.Values, usuario =>
            usuario.Email == email && usuario.TimeZoneId == "America/Argentina/Buenos_Aires");
    }

    [Fact]
    public async Task Registro_SinZonaHoraria_Devuelve400()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/usuarios", new
        {
            nombre = "Nueva persona",
            email = $"{Guid.NewGuid()}@example.invalid",
            password = "fictitious-registration-password"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registro_ConZonaMayorAlLimite_Devuelve400()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/usuarios", new
        {
            nombre = "Nueva persona",
            email = $"{Guid.NewGuid()}@example.invalid",
            password = "fictitious-registration-password",
            timeZoneId = new string('x', 101)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Invalid/Unknown")]
    [InlineData("Eastern Standard Time")]
    public async Task Registro_ConZonaNoIanaValida_Devuelve400(string timeZoneId)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/usuarios", new
        {
            nombre = "Nueva persona",
            email = $"{Guid.NewGuid()}@example.invalid",
            password = "fictitious-registration-password",
            timeZoneId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CambiarZonaHoraria_SinBearer_Devuelve401()
    {
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/usuarios/mi-zona-horaria",
            new CambiarZonaHorariaRequest { TimeZoneId = "Etc/UTC" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CambiarZonaHoraria_UsaElUsuarioDelTokenEIgnoraUsuarioIdDelBody()
    {
        var otroUsuario = new Usuario(
            "Otra persona",
            $"{Guid.NewGuid()}@example.invalid",
            "test-hash",
            "Etc/UTC");
        factory.UsuariosPersistidos[otroUsuario.Id] = otroUsuario;
        using var client = ClienteAutenticado(factory.Usuario.Id);

        try
        {
            var response = await client.PutAsJsonAsync("/api/usuarios/mi-zona-horaria", new
            {
                timeZoneId = "Asia/Tokyo",
                usuarioId = otroUsuario.Id
            });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal("Asia/Tokyo", factory.Usuario.TimeZoneId);
            Assert.Equal("Etc/UTC", otroUsuario.TimeZoneId);
        }
        finally
        {
            factory.Usuario.CambiarZonaHoraria("Etc/UTC");
        }
    }

    [Fact]
    public async Task CambiarZonaHoraria_ConZonaInvalida_Devuelve400YSinCambios()
    {
        var timeZoneInicial = factory.Usuario.TimeZoneId;
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.PutAsJsonAsync(
            "/api/usuarios/mi-zona-horaria",
            new CambiarZonaHorariaRequest { TimeZoneId = "Eastern Standard Time" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(timeZoneInicial, factory.Usuario.TimeZoneId);
    }

    [Fact]
    public async Task CambiarZonaHoraria_ConZonaMayorAlLimite_Devuelve400YSinCambios()
    {
        var timeZoneInicial = factory.Usuario.TimeZoneId;
        var guardadosAntes = factory.CantidadDeCambiosGuardados;
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.PutAsJsonAsync(
            "/api/usuarios/mi-zona-horaria",
            new CambiarZonaHorariaRequest { TimeZoneId = new string('x', 101) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(timeZoneInicial, factory.Usuario.TimeZoneId);
        Assert.Equal(guardadosAntes, factory.CantidadDeCambiosGuardados);
    }

    [Fact]
    public async Task CambiarZonaHoraria_ConTokenDeUsuarioInexistente_Devuelve401YSinCambios()
    {
        var otroUsuario = new Usuario(
            "Usuario existente",
            $"{Guid.NewGuid()}@example.invalid",
            "test-hash",
            "Etc/UTC");
        factory.UsuariosPersistidos[otroUsuario.Id] = otroUsuario;
        var otroUsuarioZonaInicial = otroUsuario.TimeZoneId;
        var usuarioZonaInicial = factory.Usuario.TimeZoneId;
        var guardadosAntes = factory.CantidadDeCambiosGuardados;
        using var client = ClienteAutenticado(Guid.NewGuid());

        var response = await client.PutAsJsonAsync(
            "/api/usuarios/mi-zona-horaria",
            new CambiarZonaHorariaRequest { TimeZoneId = "Asia/Tokyo" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(guardadosAntes, factory.CantidadDeCambiosGuardados);
        Assert.Equal(usuarioZonaInicial, factory.Usuario.TimeZoneId);
        Assert.Equal(otroUsuarioZonaInicial, otroUsuario.TimeZoneId);
    }

    private HttpClient ClienteAutenticado(Guid subject)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CrearToken(subject));
        return client;
    }

    private string CrearToken(Guid subject)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new[]
        {
            new Claim("sub", subject.ToString()),
            new Claim("name", factory.Usuario.Nombre),
            new Claim("email", factory.Usuario.Email),
            new Claim("iat", now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim("jti", Guid.NewGuid().ToString())
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            JwtApiFactory.TestIssuer,
            JwtApiFactory.TestAudience,
            claims,
            now.AddMinutes(-1).UtcDateTime,
            now.AddMinutes(5).UtcDateTime,
            credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
