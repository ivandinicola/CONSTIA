using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Constia.API.Contracts;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure.Habitos;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Constia.API.Tests.ConsultaDiariaSqlServerTests;
using static Constia.API.Tests.RegistrarCumplimientoSqlServerTests;

namespace Constia.API.Tests;

public sealed class HistorialCumplimientosSqlServerTests(ITestOutputHelper output)
{
    [SqlServerIntegrationFact]
    public async Task Get_FiltraPorHabitoYPropietario_YDistingueVacioDe404()
    {
        var database = await CreateReadyDatabaseAsync();
        try
        {
            var fecha = new DateOnly(2026, 10, 9);
            var propietario = new Usuario("Propietario", "comp006-propio@example.invalid", "hash-de-prueba", "Etc/UTC");
            var otro = new Usuario("Otro", "comp006-ajeno@example.invalid", "hash-de-prueba", "Etc/UTC");
            var propio = HabitoHistorico.Crear(propietario, "Propio", null, fecha.AddDays(-7), [DayOfWeek.Friday]);
            propio.ActualizarConfiguracionVersionada(propio.Nombre, propio.Descripcion, [DayOfWeek.Saturday], fecha);
            propio.Desactivar(fecha.AddDays(1));
            var otroPropio = HabitoHistorico.Crear(propietario, "Otro propio", null, fecha, [DayOfWeek.Friday]);
            var vacio = HabitoHistorico.Crear(propietario, "Vacío", null, fecha, [DayOfWeek.Friday]);
            var ajeno = HabitoHistorico.Crear(otro, "Ajeno", null, fecha, [DayOfWeek.Friday]);
            var reciente = new Cumplimiento(propio, fecha);
            var antiguo = new Cumplimiento(propio, fecha.AddDays(-7));

            await using (var db = CreateContext(database.ConnectionString))
            {
                db.AddRange(propietario, otro);
                await db.SaveChangesAsync();
                var habitos = new HabitoRepository(db);
                foreach (var habito in new[] { propio, otroPropio, vacio, ajeno })
                {
                    await habitos.AgregarAsync(habito, CancellationToken.None);
                }

                db.AddRange(antiguo, reciente, new Cumplimiento(otroPropio, fecha), new Cumplimiento(ajeno, fecha));
                await db.SaveChangesAsync();
            }

            var consultas = new ConsultasEjecutadas();
            using var host = new HostSql(database.ConnectionString, consultas);
            using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            using (var scope = host.Services.CreateScope())
            {
                var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                    tokens.Emitir(propietario.Id, propietario.Nombre, propietario.Email).Value);
            }

            using var respuesta = await client.GetAsync(Ruta(propio.Id));
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            var historial = Assert.IsType<CumplimientoResponse[]>(
                await respuesta.Content.ReadFromJsonAsync<CumplimientoResponse[]>());
            Assert.Equal(
                [new CumplimientoResponse(reciente.Id, reciente.Fecha), new CumplimientoResponse(antiguo.Id, antiguo.Fecha)],
                historial);
            var consulta = Assert.Single(consultas.Comandos);

            using var sinRegistros = await client.GetAsync(Ruta(vacio.Id));
            Assert.Equal(HttpStatusCode.OK, sinRegistros.StatusCode);
            Assert.Empty(Assert.IsType<CumplimientoResponse[]>(
                await sinRegistros.Content.ReadFromJsonAsync<CumplimientoResponse[]>()));
            using var ajenoRespuesta = await client.GetAsync(Ruta(ajeno.Id));
            using var inexistente = await client.GetAsync(Ruta(Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.NotFound, ajenoRespuesta.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
            Assert.Equal(4, consultas.Comandos.Count);
            output.WriteLine("Propio inactivo: 200, dos hechos en orden. Propio vacío: 200 []. Ajeno e inexistente: 404.");
            output.WriteLine("Cuatro solicitudes, cuatro lecturas EF.");
            output.WriteLine(consulta);
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    private static string Ruta(Guid id) => $"/api/habitos/{id:D}/cumplimientos";
}
