using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure.Habitos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Constia.API.Tests.RegistrarCumplimientoSqlServerTests;

namespace Constia.API.Tests;

public sealed class EditarHabitoSqlServerTests(ITestOutputHelper output)
{
    [SqlServerIntegrationFact]
    public async Task DosEdicionesConElMismoETag_RechazaLaSegundaSinSobrescribir()
    {
        var database = await CreateReadyDatabaseAsync();
        try
        {
            var usuario = new Usuario("UI007", "ui007@example.invalid", "hash-sintetico", "Etc/UTC");
            var habito = HabitoHistorico.Crear(usuario, "Original", null, new DateOnly(2026, 10, 1), [DayOfWeek.Monday]);
            await using (var seed = CreateContext(database.ConnectionString))
            {
                await new HabitoRepository(seed).AgregarAsync(habito, default);
            }

            using var host = new ConsultaDiariaSqlServerTests.HostSql(database.ConnectionString, new());
            using var clientA = host.CreateClient(new() { BaseAddress = new("https://localhost") });
            using var clientB = host.CreateClient(new() { BaseAddress = new("https://localhost") });
            using (var scope = host.Services.CreateScope())
            {
                var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
                foreach (var client in new[] { clientA, clientB })
                    client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.Emitir(usuario.Id, usuario.Nombre, usuario.Email).Value);
            }

            var ruta = $"/api/habitos/{habito.Id:D}";
            using var detalleA = await clientA.GetAsync(ruta);
            using var detalleB = await clientB.GetAsync(ruta);
            Assert.Equal(HttpStatusCode.OK, detalleA.StatusCode);
            Assert.Equal(HttpStatusCode.OK, detalleB.StatusCode);
            var tag = Assert.IsType<EntityTagHeaderValue>(detalleA.Headers.ETag);
            Assert.Equal(tag, detalleB.Headers.ETag);

            using var primero = await GuardarAsync(clientA, "Primero", "Cambios de A", DayOfWeek.Friday, tag);
            using var segundo = await GuardarAsync(clientB, "Segundo", "Cambios de B", DayOfWeek.Saturday, tag);
            Assert.Equal(HttpStatusCode.OK, primero.StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed, segundo.StatusCode);
            await using (var db = CreateContext(database.ConnectionString))
            {
                var persistido = await new HabitoRepository(db).BuscarPorIdYUsuarioAsync(habito.Id, usuario.Id, default);
                Assert.NotNull(persistido);
                Assert.Equal("Primero", persistido.Nombre);
                Assert.Equal("Cambios de A", persistido.Descripcion);
                Assert.Equal([DayOfWeek.Friday], persistido.DiasProgramados.Select(d => d.Dia));
                Assert.Equal(2, persistido.ConcurrencyVersion);
            }

            using var legado = await GuardarAsync(clientB, "Cliente anterior", null, DayOfWeek.Friday, null);
            Assert.Equal(HttpStatusCode.OK, legado.StatusCode);
            await using var verificacion = CreateContext(database.ConnectionString);
            var final = await verificacion.Set<Habito>().SingleAsync(h => h.Id == habito.Id);
            Assert.Equal("Cliente anterior", final.Nombre);
            Assert.Equal(3, final.ConcurrencyVersion);
            output.WriteLine("Dos GET con el mismo ETag. Primer PUT 200 y persistido; segundo PUT 412. PUT sin If-Match 200.");

            async Task<HttpResponseMessage> GuardarAsync(HttpClient client, string nombre, string? descripcion,
                DayOfWeek dia, EntityTagHeaderValue? version)
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, ruta);
                if (version is not null) request.Headers.IfMatch.Add(version);
                request.Content = JsonContent.Create(new { nombre, descripcion, diasProgramados = new[] { dia } });
                return await client.SendAsync(request);
            }
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
            output.WriteLine("Base temporal eliminada por el fixture.");
        }
    }
}
