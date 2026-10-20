using System.Net;
using System.Text;
using System.Text.Json;
using Constia.Domain;

namespace Constia.API.Tests;

public sealed class HabCat02HttpTests
{
    private const string Category = "82c0b8e6-0b9d-4e18-9bf8-000000000005";
    public static IEnumerable<object[]> InvalidJson()
    {
        foreach (var field in new[] { "categoriaId", "iconoId" })
        {
            var value = field == "categoriaId" ? $"\"{Category}\"" : "\"libro\"";
            foreach (var fragment in new[] {
                $"\"{field}\":null",
                $"\"{field}\":{value},\"{field}\":{value}",
                $"\"{field}\":{value},\"{field.ToUpperInvariant()}\":{value}",
                $"\"{field}\":{value},\"{field}\":null",
                $"\"{field}\":null,\"{field}\":{value}",
                $"\"{field}\":true", $"\"{field}\":123", $"\"{field}\":{{}}",
                $"\"{field}\":[]"
            }) yield return [fragment, field];
            yield return [$"\"{field}\":null,\"{(field == "categoriaId" ? "categoriaIdInformada" : "iconoIdInformado")}\":false", field];
            var escaped = field.Replace("Id", @"\u0049d", StringComparison.Ordinal);
            yield return [$"\"{field}\":{value},\"{escaped}\":{value}", field];
        }
        foreach (var value in new[] { "", " ", "Libro", " libro ", "LIBRO", "desconocido" })
            yield return [$"\"iconoId\":{JsonSerializer.Serialize(value)}", "iconoId"];
        foreach (var value in new[] { Guid.Empty.ToString(), Guid.NewGuid().ToString(), "mal-formado" })
            yield return [$"\"categoriaId\":\"{value}\"", "categoriaId"];
        yield return ["\"categoriaId\":null,\"iconoId\":null", "categoriaId"];
    }

    [Theory]
    [MemberData(nameof(InvalidJson))]
    public async Task MetadataInvalida_Devuelve400SinMutarNiGuardar(string fragment, string field)
    {
        using var factory = new JwtApiFactory();
        var habit = NewHabit(factory);
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var response = await client.PutAsync($"/api/habitos/{habit.Id}", Json(fragment));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
        Assert.True(body.RootElement.TryGetProperty("errors", out var errors));
        Assert.Contains(errors.EnumerateObject(), p =>
            p.Name.Equals(field, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals("$." + field, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Leer", habit.Nombre);
        Assert.Null(habit.CategoriaId);
        Assert.Null(habit.IconoId);
        Assert.Single(habit.Programaciones);
        Assert.Equal(0, factory.CantidadDeHabitosGuardados);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsignarConJsonReal_ConservaContratoFechasYProgramacion(bool inactive)
    {
        using var factory = new JwtApiFactory();
        var habit = NewHabit(factory);
        if (inactive) habit.Desactivar(habit.FechaInicio);
        var schedule = Assert.Single(habit.Programaciones);
        var stateHistory = habit.PeriodosEstado.ToArray();
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var response = await client.PutAsync($"/api/habitos/{habit.Id}",
            Json($"\"categoriaId\":\"{Category}\",\"iconoId\":\"billete\""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Guid.Parse(Category), habit.CategoriaId);
        Assert.Equal("billete", habit.IconoId);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        HabitoMetadataHttpTests.VerificarContrato(body.RootElement, habit, Guid.Parse(Category), "billete");
        Assert.Same(schedule, Assert.Single(habit.Programaciones));
        Assert.Equal(stateHistory, habit.PeriodosEstado);
        Assert.Equal(inactive ? EstadoHabito.Inactivo : EstadoHabito.Activo, habit.Estado);
        Assert.Equal(1, factory.CantidadDeHabitosGuardados);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"iconoId\":\"agua\"")]
    [InlineData("\"categoriaId\":\"82c0b8e6-0b9d-4e18-9bf8-000000000002\"")]
    [InlineData("\"categoriaIdInformada\":true,\"iconoIdInformado\":true,\"fechaInicio\":\"1900-01-01\",\"usuarioId\":null")]
    public async Task Omision_ConservaLosValoresNoInformados(string fragment)
    {
        using var factory = new JwtApiFactory();
        var habit = HabitoMetadataMaterialization.Crear(factory.Usuario, Guid.Parse(Category), "libro");
        factory.ReemplazarHabitos([habit]);
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var response = await client.PutAsync($"/api/habitos/{habit.Id}", Json(fragment));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(fragment.Contains("\"categoriaId\":") ?
            CatalogoCategoriasHabito.Todas[1].Id : Guid.Parse(Category), habit.CategoriaId);
        Assert.Equal(fragment.Contains("\"iconoId\":") ? "agua" : "libro", habit.IconoId);
        Assert.Equal(new DateOnly(2026, 10, 1), habit.FechaInicio);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        HabitoMetadataHttpTests.VerificarContrato(body.RootElement, habit, habit.CategoriaId, habit.IconoId);
    }

    [Theory]
    [InlineData("\"nombre\":\"Cambio\"")]
    [InlineData("\"diasProgramados\":[1]")]
    public async Task Put_SigueRequiriendoNombreYDias(string json)
    {
        using var factory = new JwtApiFactory();
        var habit = NewHabit(factory);
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var response = await client.PutAsync($"/api/habitos/{habit.Id}",
            new StringContent("{" + json + "}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.CantidadDeHabitosGuardados);
    }

    [Fact]
    public async Task CategoriaOficialAusente_DevuelveValidationProblemSinMutacion()
    {
        using var factory = new JwtApiFactory();
        var habit = NewHabit(factory);
        factory.CategoriasNoDisponibles.Add(Guid.Parse(Category));
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var response = await client.PutAsync($"/api/habitos/{habit.Id}",
            Json($"\"categoriaId\":\"{Category}\",\"iconoId\":\"libro\""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("categoriaId", out _));
        Assert.Equal("Leer", habit.Nombre);
        Assert.Null(habit.CategoriaId);
        Assert.Null(habit.IconoId);
        Assert.Equal(0, factory.CantidadDeHabitosGuardados);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AjenoOInexistente_Devuelve404SinGuardar(bool foreign)
    {
        using var factory = new JwtApiFactory();
        var habit = foreign
            ? HabitoHistorico.Crear(new Usuario("Otro", "otro@example.invalid", "test-hash", "Etc/UTC"),
                "Privado", null, new DateOnly(2026,10,1), [DayOfWeek.Monday])
            : NewHabit(factory);
        factory.ReemplazarHabitos([habit]);
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var id = foreign ? habit.Id : Guid.NewGuid();
        var response = await client.PutAsync($"/api/habitos/{id}", Json("\"iconoId\":\"agua\""));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(habit.IconoId);
        Assert.Equal(0, factory.CantidadDeHabitosGuardados);
    }

    [Fact]
    public async Task SinJwt_Devuelve401SinGuardar()
    {
        using var factory = new JwtApiFactory();
        var habit = NewHabit(factory);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsync($"/api/habitos/{habit.Id}", Json("\"iconoId\":\"agua\""))).StatusCode);
        Assert.Null(habit.IconoId);
        Assert.Equal(0, factory.CantidadDeHabitosGuardados);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricoOmitido_ConservaNullDescripcionAusente(bool nullDescription)
    {
        using var factory = new JwtApiFactory();
        var habit = NewHabit(factory);
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory);
        var json = "{\"nombre\":\"Leer\",\"diasProgramados\":[1]" +
            (nullDescription ? ",\"descripcion\":null" : "") + "}";
        var response = await client.PutAsync($"/api/habitos/{habit.Id}",
            new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(habit.CategoriaId);
        Assert.Null(habit.IconoId);
        Assert.Null(habit.Descripcion);
    }

    private static Habito NewHabit(JwtApiFactory factory)
    {
        var habit = HabitoHistorico.Crear(factory.Usuario, "Leer", "Historia", new DateOnly(2026,10,1), [DayOfWeek.Monday]);
        factory.ReemplazarHabitos([habit]);
        return habit;
    }
    private static StringContent Json(string fragment) => new(
        "{\"nombre\":\"Cambio\",\"descripcion\":null,\"diasProgramados\":[1]" +
        (fragment.Length == 0 ? "" : "," + fragment) + "}", Encoding.UTF8, "application/json");
}
