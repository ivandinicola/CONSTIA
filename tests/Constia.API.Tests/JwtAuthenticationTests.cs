using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Constia.API.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Constia.API.Tests;

public sealed class JwtAuthenticationTests(JwtApiFactory factory) : IClassFixture<JwtApiFactory>
{
    [Fact]
    public async Task LoginEmiteTokenValido_ConClaimsMinimosYSubDelUsuario()
    {
        var respuesta = await ObtenerTokenDeLoginExitosoAsync();

        Assert.False(string.IsNullOrWhiteSpace(respuesta.AccessToken));
        Assert.True(respuesta.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal(factory.Usuario.Id, respuesta.UserId);
        Assert.Equal(factory.Usuario.Nombre, respuesta.Name);
        Assert.Equal(factory.Usuario.Email, respuesta.Email);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(respuesta.AccessToken);
        Assert.Equal(factory.Usuario.Id.ToString(), jwt.Claims.Single(claim => claim.Type == "sub").Value);
        Assert.Equal(factory.Usuario.Nombre, jwt.Claims.Single(claim => claim.Type == "name").Value);
        Assert.Equal(factory.Usuario.Email, jwt.Claims.Single(claim => claim.Type == "email").Value);
        Assert.False(string.IsNullOrWhiteSpace(jwt.Claims.Single(claim => claim.Type == "iat").Value));
        Assert.False(string.IsNullOrWhiteSpace(jwt.Claims.Single(claim => claim.Type == "exp").Value));
        Assert.False(string.IsNullOrWhiteSpace(jwt.Claims.Single(claim => claim.Type == "jti").Value));
        Assert.Equal(JwtApiFactory.TestIssuer, jwt.Issuer);
        Assert.Contains(JwtApiFactory.TestAudience, jwt.Audiences);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type.Equals("TimeZoneId", StringComparison.OrdinalIgnoreCase));

        var autenticacion = await AutenticarAsync(respuesta.AccessToken);
        Assert.True(autenticacion.Succeeded, autenticacion.Failure?.Message);
        Assert.Equal(
            factory.Usuario.Id.ToString(),
            autenticacion.Principal!.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Bearer_RechazaTokenConFirmaAlterada()
    {
        var respuesta = await ObtenerTokenDeLoginExitosoAsync();
        var partes = respuesta.AccessToken.Split('.');
        Assert.Equal(3, partes.Length);
        partes[2] = (partes[2][0] == 'A' ? "B" : "A") + partes[2][1..];

        var autenticacion = await AutenticarAsync(string.Join('.', partes));

        Assert.False(autenticacion.Succeeded);
    }

    [Fact]
    public async Task Bearer_RechazaTokenExpirado()
    {
        var token = CrearTokenFirmado(
            JwtApiFactory.TestIssuer,
            JwtApiFactory.TestAudience,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        var autenticacion = await AutenticarAsync(token);

        Assert.False(autenticacion.Succeeded);
    }

    [Fact]
    public async Task Bearer_RechazaTokenConIssuerIncorrecto()
    {
        var token = CrearTokenFirmado(
            "issuer-incorrecto",
            JwtApiFactory.TestAudience,
            DateTimeOffset.UtcNow.AddMinutes(5));

        var autenticacion = await AutenticarAsync(token);

        Assert.False(autenticacion.Succeeded);
    }

    [Fact]
    public async Task Bearer_RechazaTokenConAudienceIncorrecta()
    {
        var token = CrearTokenFirmado(
            JwtApiFactory.TestIssuer,
            "audience-incorrecta",
            DateTimeOffset.UtcNow.AddMinutes(5));

        var autenticacion = await AutenticarAsync(token);

        Assert.False(autenticacion.Succeeded);
    }

    private async Task<LoginUsuarioResponse> ObtenerTokenDeLoginExitosoAsync()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login",
            new { email = factory.Usuario.Email, password = JwtApiFactory.TestPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginUsuarioResponse>())!;
    }

    private async Task<AuthenticateResult> AutenticarAsync(string token)
    {
        using var scope = factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = $"Bearer {token}";

        return await context.AuthenticateAsync("Bearer");
    }

    private string CrearTokenFirmado(string issuer, string audience, DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        var notBefore = expiresAt <= now ? expiresAt.AddMinutes(-1) : now.AddMinutes(-1);
        var claims = new[]
        {
            new Claim("sub", factory.Usuario.Id.ToString()),
            new Claim("name", factory.Usuario.Nombre),
            new Claim("email", factory.Usuario.Email),
            new Claim("iat", now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim("jti", Guid.NewGuid().ToString())
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
