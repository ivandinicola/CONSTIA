using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Constia.API.Contracts;
using Constia.Application.Habitos;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Constia.API.Tests;

public sealed class IconosHabitoControllerTests
{
    private static readonly (string Id, string Nombre, int Orden)[] Esperados =
    [
        ("libro", "Libro", 1),
        ("fuerza", "Fuerza", 2),
        ("agua", "Agua", 3),
        ("loto", "Loto", 4),
        ("manzana", "Manzana", 5),
        ("luna", "Luna", 6),
        ("cafe", "Café", 7),
        ("corredor", "Corredor", 8),
        ("corazon", "Corazón", 9),
        ("cerebro", "Cerebro", 10),
        ("sol", "Sol", 11),
        ("huella", "Huella", 12),
        ("brote", "Brote", 13),
        ("portatil", "Portátil", 14),
        ("musica", "Música", 15),
        ("billete", "Billete", 16)
    ];

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

        var response = await client.GetAsync("/api/habitos/iconos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listar_ConJwtValido_DevuelveLosDieciseisIconosYSoloLosTresCamposAprobados()
    {
        using var factory = new JwtApiFactory();
        using var client = factory.CreateClient();
        Autenticar(client, factory);

        var response = await client.GetAsync("/api/habitos/iconos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var iconos = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(16, iconos.Length);
        for (var index = 0; index < iconos.Length; index++)
        {
            var icono = iconos[index];
            Assert.Equal(["id", "nombre", "orden"],
                icono.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
            Assert.Equal(Esperados[index].Id, icono.GetProperty("id").GetString());
            Assert.Equal(Esperados[index].Nombre, icono.GetProperty("nombre").GetString());
            Assert.Equal(Esperados[index].Orden, icono.GetProperty("orden").GetInt32());
        }
    }

    [Fact]
    public async Task Listar_SinReadinessNiAccesoASql_NoModificaHabitosNiCumplimientos()
    {
        using var factory = new JwtApiFactory { SnapshotCapturado = false, HistorialCompleto = false };
        var habito = HabitoHistorico.Crear(factory.Usuario, "Leer", null, new DateOnly(2026, 10, 1), [DayOfWeek.Monday]);
        factory.ReemplazarHabitos([habito]);
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ConstiaDbContext>();
            services.AddScoped<ConstiaDbContext>(_ =>
                throw new InvalidOperationException("El catálogo de íconos no debe acceder a DbContext."));
            services.RemoveAll<IEstadoHabitoReadinessRepository>();
            services.AddScoped<IEstadoHabitoReadinessRepository, ReadinessProhibido>();
        }));
        using var client = host.CreateClient();
        Autenticar(client, factory);

        var response = await client.GetAsync("/api/habitos/iconos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(16, document.RootElement.GetArrayLength());
        Assert.Same(habito, Assert.Single(factory.HabitosPersistidos));
        Assert.Equal("Leer", habito.Nombre);
        Assert.Equal(EstadoHabito.Activo, habito.Estado);
        Assert.Null(habito.CategoriaId);
        Assert.Empty(factory.CumplimientosPersistidos);
        Assert.Equal(0, factory.CantidadDeCambiosGuardados);
        Assert.Equal(0, factory.CantidadDeHabitosGuardados);
        Assert.Equal(0, factory.CantidadDeCumplimientosGuardados);
    }

    [Fact]
    public async Task Listar_CoexisteConCategoriasInactivosYDetallePorGuid()
    {
        using var factory = new JwtApiFactory();
        var habito = HabitoHistorico.Crear(factory.Usuario, "Leer", null, new DateOnly(2026, 10, 1), [DayOfWeek.Monday]);
        factory.ReemplazarHabitos([habito]);
        using var client = factory.CreateClient();
        Autenticar(client, factory);

        var iconos = await client.GetAsync("/api/habitos/iconos");
        var categorias = await client.GetAsync("/api/habitos/categorias");
        var inactivos = await client.GetAsync("/api/habitos/inactivos");
        var detalle = await client.GetAsync($"/api/habitos/{habito.Id:D}");

        Assert.Equal(HttpStatusCode.OK, iconos.StatusCode);
        Assert.Equal(HttpStatusCode.OK, categorias.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inactivos.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detalle.StatusCode);
        using var categoriasJson = JsonDocument.Parse(await categorias.Content.ReadAsStringAsync());
        Assert.Equal(10, categoriasJson.RootElement.GetArrayLength());
        Assert.Empty((await inactivos.Content.ReadFromJsonAsync<HabitoResponse[]>())!);
        var resultado = await detalle.Content.ReadFromJsonAsync<HabitoResponse>();
        Assert.NotNull(resultado);
        Assert.Equal(habito.Id, resultado.Id);
    }

    private static void Autenticar(HttpClient client, JwtApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var usuario = factory.Usuario;
        var token = scope.ServiceProvider.GetRequiredService<IAccessTokenService>()
            .Emitir(usuario.Id, usuario.Nombre, usuario.Email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }

    private sealed class ReadinessProhibido : IEstadoHabitoReadinessRepository
    {
        public Task<bool> SnapshotCapturadoAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("El catálogo de íconos no debe consultar el snapshot.");

        public Task<bool> HistorialCompletoAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("El catálogo de íconos no debe consultar el historial.");
    }
}
