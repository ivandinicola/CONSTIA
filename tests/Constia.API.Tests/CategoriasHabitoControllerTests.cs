using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Constia.API.Contracts;
using Constia.Application.Habitos;
using Constia.Application.Usuarios;
using Constia.Domain;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Constia.API.Tests;

public sealed class CategoriasHabitoControllerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("invalid-token")]
    public async Task Listar_SinJwtValido_Devuelve401(string? token)
    {
        using var factory = new JwtApiFactory();
        using var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await client.GetAsync("/api/habitos/categorias");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listar_ConJwtValido_DevuelveContratoOficialOrdenadoYSoloTresCampos()
    {
        using var factory = new JwtApiFactory();
        using var client = ClienteAutenticado(factory, factory.Usuario);

        var response = await client.GetAsync("/api/habitos/categorias");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var categorias = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(10, categorias.Length);
        string[] nombres =
        [
            "Salud y movimiento", "Alimentación e hidratación", "Descanso y sueño",
            "Bienestar emocional", "Aprendizaje y crecimiento", "Productividad y organización",
            "Relaciones y vínculos", "Finanzas personales", "Creatividad y ocio", "Hogar y vida cotidiana"
        ];
        string[] ids =
        [
            "82c0b8e6-0b9d-4e18-9bf8-000000000001",
            "82c0b8e6-0b9d-4e18-9bf8-000000000002",
            "82c0b8e6-0b9d-4e18-9bf8-000000000003",
            "82c0b8e6-0b9d-4e18-9bf8-000000000004",
            "82c0b8e6-0b9d-4e18-9bf8-000000000005",
            "82c0b8e6-0b9d-4e18-9bf8-000000000006",
            "82c0b8e6-0b9d-4e18-9bf8-000000000007",
            "82c0b8e6-0b9d-4e18-9bf8-000000000008",
            "82c0b8e6-0b9d-4e18-9bf8-000000000009",
            "82c0b8e6-0b9d-4e18-9bf8-000000000010"
        ];
        for (var index = 0; index < categorias.Length; index++)
        {
            var categoria = categorias[index];
            Assert.Equal(["id", "nombre", "orden"],
                categoria.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
            Assert.Equal(Guid.Parse(ids[index]), categoria.GetProperty("id").GetGuid());
            Assert.Equal(nombres[index], categoria.GetProperty("nombre").GetString());
            Assert.Equal(index + 1, categoria.GetProperty("orden").GetInt32());
        }
    }

    [Fact]
    public async Task Listar_ConUsuariosDistintos_DevuelveElMismoCatalogoSinEscrituras()
    {
        using var factory = new JwtApiFactory();
        var otroUsuario = new Usuario("Otra persona", "otra@example.invalid", "fake-hash", "Etc/UTC");
        factory.UsuariosPersistidos[otroUsuario.Id] = otroUsuario;
        using var primero = ClienteAutenticado(factory, factory.Usuario);
        using var segundo = ClienteAutenticado(factory, otroUsuario);

        var responsePrimero = await primero.GetAsync("/api/habitos/categorias");
        var responseSegundo = await segundo.GetAsync("/api/habitos/categorias");

        Assert.Equal(HttpStatusCode.OK, responsePrimero.StatusCode);
        Assert.Equal(HttpStatusCode.OK, responseSegundo.StatusCode);
        Assert.Equal(await responsePrimero.Content.ReadAsStringAsync(), await responseSegundo.Content.ReadAsStringAsync());
        Assert.Equal(2, factory.UsuariosPersistidos.Count);
        Assert.Empty(factory.HabitosPersistidos);
        Assert.Empty(factory.CumplimientosPersistidos);
        Assert.Equal(0, factory.CantidadDeCambiosGuardados);
        Assert.Equal(0, factory.CantidadDeHabitosGuardados);
        Assert.Equal(0, factory.CantidadDeCumplimientosGuardados);
    }

    [Fact]
    public async Task Listar_SinReadiness_NoConsultaSnapshotNiHistorial()
    {
        using var factory = new JwtApiFactory { SnapshotCapturado = false, HistorialCompleto = false };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEstadoHabitoReadinessRepository>();
            services.AddScoped<IEstadoHabitoReadinessRepository, ReadinessProhibido>();
        }));
        using var client = host.CreateClient();
        Autenticar(client, factory, factory.Usuario);

        var response = await client.GetAsync("/api/habitos/categorias");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(10, document.RootElement.GetArrayLength());
        Assert.Equal(0, factory.CantidadDeHabitosGuardados);
    }

    [Fact]
    public async Task Listar_CoexisteConLaRutaDeDetallePorGuid()
    {
        using var factory = new JwtApiFactory();
        var habito = HabitoHistorico.Crear(factory.Usuario, "Leer", null, new DateOnly(2026, 10, 1), [DayOfWeek.Monday]);
        factory.ReemplazarHabitos([habito]);
        using var client = ClienteAutenticado(factory, factory.Usuario);

        var categorias = await client.GetAsync("/api/habitos/categorias");
        var detalle = await client.GetAsync($"/api/habitos/{habito.Id:D}");

        Assert.Equal(HttpStatusCode.OK, categorias.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detalle.StatusCode);
        var body = await detalle.Content.ReadFromJsonAsync<HabitoResponse>();
        Assert.NotNull(body);
        Assert.Equal(habito.Id, body.Id);
        Assert.Equal("Leer", body.Nombre);
        Assert.Same(habito, Assert.Single(factory.HabitosPersistidos));
        Assert.Equal(0, factory.CantidadDeHabitosGuardados);
    }

    private static HttpClient ClienteAutenticado(JwtApiFactory factory, Usuario usuario)
    {
        var client = factory.CreateClient();
        Autenticar(client, factory, usuario);
        return client;
    }

    private static void Autenticar(HttpClient client, JwtApiFactory factory, Usuario usuario)
    {
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IAccessTokenService>()
            .Emitir(usuario.Id, usuario.Nombre, usuario.Email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }

    private sealed class ReadinessProhibido : IEstadoHabitoReadinessRepository
    {
        public Task<bool> SnapshotCapturadoAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("El catálogo no debe consultar el snapshot.");

        public Task<bool> HistorialCompletoAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("El catálogo no debe consultar el historial.");
    }
}
