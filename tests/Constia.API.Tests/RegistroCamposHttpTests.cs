using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Constia.Application.Correo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Constia.API.Tests;

public sealed class RegistroCamposHttpTests
{
    public static IEnumerable<object[]> Obligatorios()
    {
        foreach (var campo in new[] { "nombre", "email", "password", "timeZoneId" })
            foreach (var variante in new[] { "omitido", "null", "vacio", "blanco" })
                if (campo != "timeZoneId" || variante != "omitido") // Ya cubierto por ZonaHorariaUsuarioTests.
                    yield return [campo, variante];
    }

    [Theory]
    [MemberData(nameof(Obligatorios))]
    public async Task Registro_ObligatorioInvalido_400PorCampoSinGuardar(string campo, string variante)
    {
        var solicitud = SolicitudValida();
        if (variante == "omitido") solicitud.Remove(campo);
        else solicitud[campo] = variante switch { "null" => null, "vacio" => "", _ => "   " };
        await VerificarRechazo(solicitud, campo);
    }

    [Theory]
    [InlineData("nombre", 201)]
    [InlineData("email", 255)]
    public async Task Registro_ExcesoDeLongitud_400PorCampoSinGuardar(string campo, int longitud)
    {
        var solicitud = SolicitudValida();
        solicitud[campo] = campo == "nombre" ? new string('n', longitud) : EmailDeLongitud(longitud);
        await VerificarRechazo(solicitud, campo);
    }

    [Theory]
    [InlineData("nombre", 200)]
    [InlineData("email", 254)]
    public async Task Registro_LimiteValido_201YUnSoloGuardado(string campo, int longitud)
    {
        using var factory = new JwtApiFactory();
        using var client = factory.CreateClient();
        var solicitud = SolicitudValida();
        solicitud[campo] = campo == "nombre" ? new string('n', longitud) : EmailDeLongitud(longitud);
        var anterior = JsonSerializer.Serialize(factory.Usuario);
        using var response = await client.PostAsJsonAsync("/api/usuarios", solicitud);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "email", "id", "nombre" }, json.RootElement.EnumerateObject().Select(p => p.Name).Order());
        var id = json.RootElement.GetProperty("id").GetGuid();
        var usuario = factory.UsuariosPersistidos[id];
        Assert.Equal(solicitud["nombre"], usuario.Nombre);
        Assert.Equal(solicitud["email"], usuario.Email);
        Assert.Equal(solicitud[campo], json.RootElement.GetProperty(campo).GetString());
        Assert.Equal(2, factory.UsuariosPersistidos.Count);
        Assert.Equal(1, factory.CantidadDeUsuariosGuardados);
        Assert.Single(factory.DesafiosCorreo.Values, d => d.UsuarioId == id);
        Assert.Equal(anterior, JsonSerializer.Serialize(factory.Usuario));
        await Drenar(factory);
        Assert.Single(factory.CorreosDePrueba);
    }

    private static async Task VerificarRechazo(Dictionary<string, string?> solicitud, string campo)
    {
        using var factory = new JwtApiFactory();
        using var client = factory.CreateClient();
        var anterior = JsonSerializer.Serialize(factory.Usuario);
        using var response = await client.PostAsJsonAsync("/api/usuarios", solicitud);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problema = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problema);
        Assert.Equal(400, problema.Status);
        var clave = campo switch { "nombre" => "Nombre", "email" => "Email", "password" => "Password", _ => "TimeZoneId" };
        Assert.Equal(clave, Assert.Single(problema.Errors).Key);
        Assert.NotEmpty(problema.Errors[clave]);
        Assert.Same(factory.Usuario, Assert.Single(factory.UsuariosPersistidos).Value);
        Assert.Equal(anterior, JsonSerializer.Serialize(factory.Usuario));
        Assert.Equal(0, factory.CantidadDeUsuariosGuardados);
        Assert.Empty(factory.DesafiosCorreo);
        await Drenar(factory);
        Assert.Empty(factory.CorreosDePrueba);
    }

    private static Dictionary<string, string?> SolicitudValida() => new()
    {
        ["nombre"] = "Ana", ["email"] = "registro@example.invalid",
        ["password"] = "ABCDE1.@", ["timeZoneId"] = "Etc/UTC"
    };

    private static string EmailDeLongitud(int longitud)
    {
        var email = new string('a', 64) + "@" + new string('b', 63) + "." +
            new string('c', 63) + "." + new string('d', longitud - 201) + ".invalid";
        Assert.Equal(longitud, email.Length);
        Assert.True(MailAddress.TryCreate(email, out var direccion));
        Assert.Equal(email, direccion.Address);
        Assert.Equal("", direccion.DisplayName);
        Assert.All(email.Split('@')[1].Split('.'), label => Assert.InRange(label.Length, 1, 63));
        return email;
    }

    private static async Task Drenar(JwtApiFactory factory)
    {
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await factory.Services.GetRequiredService<ICanalVerificacionCorreo>().EsperarVacioAsync(limite.Token);
    }
}
