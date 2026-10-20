using System.Net;
using System.Text;
using System.Text.Json;
using Constia.Domain;

namespace Constia.API.Tests;

public sealed class HabDate01HttpTests
{
    private static string Categoria => CatalogoCategoriasHabito.Todas[4].Id.ToString();
    private static string Payload(string extra = "") =>
        $$"""{"nombre":" Leer ","categoriaId":"{{Categoria}}","iconoId":"billete","diasProgramados":[3,1,3]{{extra}}}""";

    [Theory]
    [InlineData("")]
    [InlineData(",\"descripcion\":null")]
    [InlineData(",\"descripcion\":\"fechaInicio no es una propiedad raíz\"")]
    [InlineData(",\"desconocido\":{\"fechaInicio\":\"2020-01-01\"},\"usuarioId\":\"00000000-0000-0000-0000-000000000001\"")]
    public async Task Post_Valido_CalculaFechaYConservaContrato(string extra)
    {
        using var factory = new JwtApiFactory { HistorialCompleto = false };
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var response = await client.PostAsync("/api/habitos", Json(Payload(extra)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var habit = Assert.Single(factory.HabitosPersistidos);
        Assert.Same(factory.Usuario, habit.Usuario);
        Assert.Equal(" Leer ", habit.Nombre);
        Assert.Equal(DateTimeOffset.Parse("2026-10-10T02:30:00Z"), habit.FechaCreacion);
        Assert.Equal(new DateOnly(2026, 10, 10), habit.FechaInicio);
        Assert.Equal(habit.FechaInicio, Assert.Single(habit.Programaciones).VigenteDesde);
        Assert.Equal(habit.FechaInicio, Assert.Single(habit.PeriodosEstado).VigenteDesde);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        HabitoMetadataHttpTests.VerificarContrato(body.RootElement, habit, Guid.Parse(Categoria), "billete");
    }

    [Theory]
    [InlineData("\"fechaInicio\":\"2026-01-01\"")]
    [InlineData("\"fechaInicio\":null")]
    [InlineData("\"FechaInicio\":null")]
    [InlineData("\"FECHAINICIO\":null")]
    [InlineData("\"fech\\u0061Inicio\":null")]
    [InlineData("\"fechaInicio\":12")]
    [InlineData("\"fechaInicio\":true")]
    [InlineData("\"fechaInicio\":[]")]
    [InlineData("\"fechaInicio\":{}")]
    [InlineData("\"fechaInicio\":\"2026-01-01\",\"fechaInicio\":null")]
    [InlineData("\"fechaInicio\":null,\"fechaInicio\":\"2026-01-01\"")]
    [InlineData("\"fechaInicio\":null,\"FechaInicio\":null")]
    public async Task Post_FechaRaizProhibida_RechazaPresenciaSinGuardar(string property)
    {
        using var factory = new JwtApiFactory();
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var response = await client.PostAsync("/api/habitos", Json(Payload("," + property)));
        await AssertError(response, "fechaInicio");
        Assert.Empty(factory.HabitosPersistidos);
        Assert.Equal(0, factory.CantidadDeHabitosGuardados);
    }

    [Theory]
    [InlineData("categoriaId", null)]
    [InlineData("categoriaId", "null")]
    [InlineData("categoriaId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("categoriaId", "\"not-a-uuid\"")]
    [InlineData("categoriaId", "\"11111111-1111-1111-1111-111111111111\"")]
    [InlineData("iconoId", null)]
    [InlineData("iconoId", "null")]
    [InlineData("iconoId", "\"\"")]
    [InlineData("iconoId", "\" \"")]
    [InlineData("iconoId", "\"desconocido\"")]
    [InlineData("iconoId", "\"Libro\"")]
    public async Task Post_MetadatosInvalidos_ValidationProblemSinGuardar(string key, string? value)
    {
        using var factory = new JwtApiFactory();
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Payload())!;
        values.Remove(key);
        if (value is not null) values[key] = JsonSerializer.Deserialize<JsonElement>(value);
        var response = await client.PostAsync("/api/habitos", Json(JsonSerializer.Serialize(values)));
        await AssertError(response, key);
        Assert.Empty(factory.HabitosPersistidos);
        Assert.Equal(0, factory.CantidadDeHabitosGuardados);
    }

    [Fact]
    public async Task Post_CategoriaOficialNoPersistida_NoGuarda()
    {
        using var factory = new JwtApiFactory();
        factory.CategoriasNoDisponibles.Add(Guid.Parse(Categoria));
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var response = await client.PostAsync("/api/habitos", Json(Payload()));
        await AssertError(response, "categoriaId");
        Assert.Empty(factory.HabitosPersistidos);
    }

    [Fact]
    public async Task Post_PropietarioInexistente_401SinGuardar()
    {
        using var factory = new JwtApiFactory();
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        factory.UsuariosPersistidos.Clear();
        var response = await client.PostAsync("/api/habitos", Json(Payload()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.HabitosPersistidos);
    }

    [Fact]
    public async Task Post_SnapshotNoCapturado_503SinGuardar()
    {
        using var factory = new JwtApiFactory { SnapshotCapturado = false };
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var response = await client.PostAsync("/api/habitos", Json(Payload()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("habit_status_history_not_initialized", body.RootElement.GetProperty("code").GetString());
        Assert.Empty(factory.HabitosPersistidos);
    }


    [Fact]
    public async Task Post_ZonaPersistidaInvalida_500SinGuardar()
    {
        using var factory = new JwtApiFactory();
        typeof(Usuario).GetProperty(nameof(Usuario.TimeZoneId))!.SetValue(factory.Usuario, "Invalid/Zone");
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var response = await client.PostAsync("/api/habitos", Json(Payload()));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(factory.HabitosPersistidos);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("[7]")]
    [InlineData("[-1]")]
    public async Task Post_DiasInvalidos_400SinGuardar(string days)
    {
        using var factory = new JwtApiFactory();
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Payload())!;
        values["diasProgramados"] = JsonSerializer.Deserialize<JsonElement>(days);
        var response = await client.PostAsync("/api/habitos", Json(JsonSerializer.Serialize(values)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.HabitosPersistidos);
    }

    [Fact]
    public async Task Post_OmisionMetadatos_UsaClavesCamelCaseExactas()
    {
        using var factory = new JwtApiFactory();
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var response = await client.PostAsync("/api/habitos", Json("{\"nombre\":\"Leer\",\"diasProgramados\":[1]}"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("categoriaId", out _));
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("iconoId", out _));
        Assert.Empty(factory.HabitosPersistidos);
    }


    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\" \"")]
    public async Task Post_NombreInvalido_400SinGuardar(string name)
    {
        using var factory = new JwtApiFactory();
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Payload())!;
        values["nombre"] = JsonSerializer.Deserialize<JsonElement>(name);
        var response = await client.PostAsync("/api/habitos", Json(JsonSerializer.Serialize(values)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.HabitosPersistidos);
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");
    private static async Task AssertError(HttpResponseMessage response, string key)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(body.RootElement.GetProperty("errors").EnumerateObject(),
            p => p.Name.Equals(key, StringComparison.Ordinal));
    }
}
