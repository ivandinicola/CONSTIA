using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Constia.API.Contracts;
using Constia.Domain;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace Constia.API.Tests;

public sealed class HabitosControllerTests(JwtApiFactory factory) : IClassFixture<JwtApiFactory>
{
    [Fact]
    public async Task DesactivarHabito_SinBearer_Devuelve401()
    {
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/api/habitos/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DesactivarHabito_ConTokenInvalido_Devuelve401()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        var response = await client.DeleteAsync($"/api/habitos/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DesactivarHabito_Propio_Devuelve204YConservaRegistroParaConsultasHistoricas()
    {
        var habit = CrearHabitoConIdYFecha(factory.Usuario, "Leer", Guid.NewGuid(), DateTimeOffset.UtcNow);
        factory.ReemplazarHabitos([habit]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var deleteResponse = await client.DeleteAsync($"/api/habitos/{habit.Id:D}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(string.Empty, await deleteResponse.Content.ReadAsStringAsync());
        Assert.Equal(EstadoHabito.Inactivo, habit.Estado);
        Assert.Contains(factory.HabitosPersistidos, persisted => ReferenceEquals(habit, persisted));

        var listResponse = await client.GetAsync("/api/habitos");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal("[]", await listResponse.Content.ReadAsStringAsync());

        var detailResponse = await client.GetAsync($"/api/habitos/{habit.Id:D}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var body = await detailResponse.Content.ReadFromJsonAsync<HabitoResponse>();
        Assert.NotNull(body);
        Assert.Equal(habit.Id, body.Id);
        Assert.Equal(EstadoHabito.Inactivo, body.Estado);
    }

    [Fact]
    public async Task DesactivarHabito_PropioYaInactivo_Devuelve204()
    {
        var habit = CrearHabitoConIdYFecha(factory.Usuario, "Leer", Guid.NewGuid(), DateTimeOffset.UtcNow);
        habit.Desactivar();
        factory.ReemplazarHabitos([habit]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.DeleteAsync($"/api/habitos/{habit.Id:D}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(EstadoHabito.Inactivo, habit.Estado);
        Assert.Contains(factory.HabitosPersistidos, persisted => ReferenceEquals(habit, persisted));
    }

    [Fact]
    public async Task DesactivarHabito_Inexistente_Devuelve404()
    {
        factory.ReemplazarHabitos([]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.DeleteAsync($"/api/habitos/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DesactivarHabito_DeOtroUsuario_Devuelve404YSinModificarlo()
    {
        var otroUsuario = new Usuario("Otro", $"{Guid.NewGuid()}@example.invalid", "fake-hash");
        var habit = CrearHabitoConIdYFecha(otroUsuario, "Privado", Guid.NewGuid(), DateTimeOffset.UtcNow);
        factory.ReemplazarHabitos([habit]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.DeleteAsync($"/api/habitos/{habit.Id:D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(EstadoHabito.Activo, habit.Estado);
    }

    [Fact]
    public async Task EditarHabito_SinBearer_Devuelve401()
    {
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/habitos/{Guid.NewGuid():D}", CrearRequestEdicion());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EditarHabito_Propio_DevuelveCambiosYSinUsuarioId()
    {
        var habit = CrearHabitoConIdYFecha(
            factory.Usuario,
            "Leer",
            Guid.NewGuid(),
            new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero));
        factory.ReemplazarHabitos([habit]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/habitos/{habit.Id:D}",
            CrearRequestEdicion("Caminar", "Nueva descripción", [DayOfWeek.Wednesday, DayOfWeek.Friday]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HabitoResponse>();
        Assert.NotNull(body);
        Assert.Equal("Caminar", body.Nombre);
        Assert.Equal("Nueva descripción", body.Descripcion);
        Assert.Equal([DayOfWeek.Wednesday, DayOfWeek.Friday], body.DiasProgramados);
        Assert.DoesNotContain("usuarioId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Caminar", habit.Nombre);
    }

    [Fact]
    public async Task EditarHabito_Inexistente_Devuelve404()
    {
        factory.ReemplazarHabitos([]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/habitos/{Guid.NewGuid():D}",
            CrearRequestEdicion());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EditarHabito_DeOtroUsuario_Devuelve404()
    {
        var usuarioAjeno = new Usuario("Otro", $"{Guid.NewGuid()}@example.invalid", "fake-hash");
        var habit = CrearHabitoConIdYFecha(usuarioAjeno, "Privado", Guid.NewGuid(), DateTimeOffset.UtcNow);
        factory.ReemplazarHabitos([habit]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/habitos/{habit.Id:D}",
            CrearRequestEdicion());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EditarHabito_ConDiasInvalidos_Devuelve400()
    {
        var habit = CrearHabitoConIdYFecha(factory.Usuario, "Leer", Guid.NewGuid(), DateTimeOffset.UtcNow);
        factory.ReemplazarHabitos([habit]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/habitos/{habit.Id:D}",
            CrearRequestEdicion(diasProgramados: []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Leer", habit.Nombre);
    }

    [Fact]
    public async Task EditarHabito_InactivoPermiteEdicionSinCambiarPropietarioEstadoNiFechas()
    {
        var originalCreatedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var originalStartDate = new DateOnly(2026, 1, 1);
        var habit = CrearHabitoConIdYFecha(factory.Usuario, "Leer", Guid.NewGuid(), originalCreatedAt);
        habit.Desactivar();
        factory.ReemplazarHabitos([habit]);
        using var client = ClienteAutenticado(factory.Usuario.Id);
        var maliciousBody = new
        {
            nombre = "Caminar",
            descripcion = "Actualizada",
            diasProgramados = new[] { (int)DayOfWeek.Friday },
            usuarioId = Guid.NewGuid(),
            estado = (int)EstadoHabito.Activo,
            fechaInicio = "2035-12-31",
            createdAt = "2035-12-31T00:00:00Z"
        };

        var response = await client.PutAsJsonAsync($"/api/habitos/{habit.Id:D}", maliciousBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HabitoResponse>();
        Assert.NotNull(body);
        Assert.Equal(EstadoHabito.Inactivo, body.Estado);
        Assert.Equal(originalCreatedAt, body.CreatedAt);
        Assert.Equal(originalStartDate, body.StartDate);
        Assert.Same(factory.Usuario, habit.Usuario);
        Assert.Equal(EstadoHabito.Inactivo, habit.Estado);
        Assert.Equal(originalCreatedAt, habit.FechaCreacion);
        Assert.Equal(originalStartDate, habit.FechaInicio);
        Assert.DoesNotContain("usuarioId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ObtenerHabitoPorId_SinBearer_Devuelve401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/habitos/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ObtenerHabitoPorId_Propio_Devuelve200ConDiasYSinUsuarioId()
    {
        var habito = CrearHabitoConIdYFecha(
            factory.Usuario,
            "Consultar libro",
            Guid.NewGuid(),
            new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero));
        factory.ReemplazarHabitos([habito]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.GetAsync($"/api/habitos/{habito.Id:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HabitoResponse>();
        Assert.NotNull(body);
        Assert.Equal(habito.Id, body.Id);
        Assert.Equal(habito.Nombre, body.Nombre);
        Assert.Equal([DayOfWeek.Monday], body.DiasProgramados);
        Assert.DoesNotContain("usuarioId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ObtenerHabitoPorId_Inexistente_Devuelve404()
    {
        factory.ReemplazarHabitos([]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.GetAsync($"/api/habitos/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ObtenerHabitoPorId_DeOtroUsuario_Devuelve404()
    {
        var usuarioAjeno = new Usuario("Otro", $"{Guid.NewGuid()}@example.invalid", "fake-hash");
        var habitoAjeno = CrearHabitoConIdYFecha(
            usuarioAjeno,
            "Privado",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        factory.ReemplazarHabitos([habitoAjeno]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.GetAsync($"/api/habitos/{habitoAjeno.Id:D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ObtenerHabitoPorId_PropioInactivo_Devuelve200()
    {
        var habito = CrearHabitoConIdYFecha(
            factory.Usuario,
            "Hábito inactivo",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        habito.Desactivar();
        factory.ReemplazarHabitos([habito]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.GetAsync($"/api/habitos/{habito.Id:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HabitoResponse>();
        Assert.NotNull(body);
        Assert.Equal(EstadoHabito.Inactivo, body.Estado);
    }

    [Fact]
    public async Task ListarHabitosActivos_SinBearer_Devuelve401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/habitos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListarHabitosActivos_SinHabitos_Devuelve200ConListaVacia()
    {
        factory.ReemplazarHabitos([]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.GetAsync("/api/habitos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ListarHabitosActivos_FiltraPorPropietarioYEstadoYOrdenaDeterministicamente()
    {
        var fechaEmpatada = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var fechaAnterior = fechaEmpatada.AddDays(-1);
        var usuarioAjeno = new Usuario("Otro", $"{Guid.NewGuid()}@example.invalid", "fake-hash");
        var desempateAlto = CrearHabitoConIdYFecha(
            factory.Usuario,
            "Empate alto",
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            fechaEmpatada);
        var anterior = CrearHabitoConIdYFecha(
            factory.Usuario,
            "Anterior",
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
            fechaAnterior);
        var desempateBajo = CrearHabitoConIdYFecha(
            factory.Usuario,
            "Empate bajo",
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            fechaEmpatada);
        var ajeno = CrearHabitoConIdYFecha(
            usuarioAjeno,
            "Ajeno",
            Guid.Parse("00000000-0000-0000-0000-000000000004"),
            fechaAnterior);
        var inactivo = CrearHabitoConIdYFecha(
            factory.Usuario,
            "Inactivo",
            Guid.Parse("00000000-0000-0000-0000-000000000005"),
            fechaAnterior);
        inactivo.Desactivar();
        factory.ReemplazarHabitos([desempateAlto, ajeno, inactivo, desempateBajo, anterior]);
        using var client = ClienteAutenticado(factory.Usuario.Id);

        var response = await client.GetAsync("/api/habitos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HabitoResponse[]>();
        Assert.NotNull(body);
        Assert.Equal(["Anterior", "Empate bajo", "Empate alto"], body.Select(habito => habito.Nombre));
        Assert.All(body, habito => Assert.NotEqual(ajeno.Id, habito.Id));
        Assert.All(body, habito => Assert.NotEqual(inactivo.Id, habito.Id));
        Assert.Equal([DayOfWeek.Monday], body[0].DiasProgramados);
        Assert.DoesNotContain("usuarioId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CrearHabito_SinBearer_Devuelve401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/habitos", CrearRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CrearHabito_ConTokenInvalido_Devuelve401()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        var response = await client.PostAsJsonAsync("/api/habitos", CrearRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CrearHabito_ConJwtValido_Devuelve201YGuardaElHabito()
    {
        using var client = ClienteAutenticado(factory.Usuario.Id);
        var nombre = $"Leer {Guid.NewGuid()}";

        var response = await client.PostAsJsonAsync("/api/habitos", CrearRequest(nombre));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HabitoResponse>();
        Assert.NotNull(body);
        Assert.Equal(nombre, body.Nombre);
        Assert.Equal(DateOnly.Parse("2026-10-06"), body.StartDate);
        Assert.Equal(EstadoHabito.Activo, body.Estado);
        Assert.DoesNotContain("usuarioId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(factory.HabitosPersistidos, habit => habit.Nombre == nombre && habit.Usuario.Id == factory.Usuario.Id);
    }

    [Fact]
    public async Task CrearHabito_IgnoraUsuarioIdEnJsonYUsaElSubDelToken()
    {
        using var client = ClienteAutenticado(factory.Usuario.Id);
        var nombre = $"Caminar {Guid.NewGuid()}";
        var otroUsuarioId = Guid.NewGuid();
        var request = new
        {
            nombre,
            descripcion = "Paseo diario",
            fechaInicio = "2026-10-06",
            diasProgramados = new[] { (int)DayOfWeek.Monday },
            usuarioId = otroUsuarioId
        };

        var response = await client.PostAsJsonAsync("/api/habitos", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains(factory.HabitosPersistidos, habit => habit.Nombre == nombre && habit.Usuario.Id == factory.Usuario.Id);
        Assert.DoesNotContain(factory.HabitosPersistidos, habit => habit.Nombre == nombre && habit.Usuario.Id == otroUsuarioId);
    }

    [Fact]
    public async Task CrearHabito_ConDiasInvalidos_Devuelve400()
    {
        using var client = ClienteAutenticado(factory.Usuario.Id);
        var request = new
        {
            nombre = "Leer",
            descripcion = "",
            fechaInicio = "2026-10-06",
            diasProgramados = Array.Empty<int>()
        };

        var response = await client.PostAsJsonAsync("/api/habitos", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registro_SigueSiendoPublico()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/usuarios", new
        {
            nombre = "Existing User",
            email = factory.Usuario.Email,
            password = JwtApiFactory.TestPassword
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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

    private static object CrearRequest(string nombre = "Leer") => new
    {
        nombre,
        descripcion = "Diez páginas",
        fechaInicio = "2026-10-06",
        diasProgramados = new[] { (int)DayOfWeek.Monday, (int)DayOfWeek.Wednesday }
    };

    private static object CrearRequestEdicion(
        string nombre = "Caminar",
        string? descripcion = "Hacer una caminata",
        DayOfWeek[]? diasProgramados = null) => new
    {
        nombre,
        descripcion,
        diasProgramados = (diasProgramados ?? [DayOfWeek.Monday, DayOfWeek.Wednesday])
            .Select(dia => (int)dia)
            .ToArray()
    };

    private static Habito CrearHabitoConIdYFecha(
        Usuario propietario,
        string nombre,
        Guid id,
        DateTimeOffset fechaCreacion)
    {
        var habito = new Habito(propietario, nombre, null, new DateOnly(2026, 1, 1), [DayOfWeek.Monday]);
        typeof(Habito).GetProperty(nameof(Habito.Id))!.SetValue(habito, id);
        typeof(Habito).GetProperty(nameof(Habito.FechaCreacion))!.SetValue(habito, fechaCreacion);
        return habito;
    }
}
