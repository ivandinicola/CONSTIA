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

public sealed class CalendarioSqlServerTests(ITestOutputHelper output)
{
    [SqlServerIntegrationFact]
    public async Task Get_AislaPropietarioYRango_YLeeEnBloqueAunqueAumentenLosDias()
    {
        var database = await CreateReadyDatabaseAsync();
        try
        {
            var desde = new DateOnly(2026, 10, 30);
            var hasta = desde.AddDays(41);
            var propietario = new Usuario("Propietario", "stat005-propio@example.invalid", "hash-de-prueba", "Etc/UTC");
            var otro = new Usuario("Otro", "stat005-ajeno@example.invalid", "hash-de-prueba", "Etc/UTC");
            var propio = HabitoHistorico.Crear(propietario, "Propio", null, desde.AddDays(-7),
                [desde.DayOfWeek, hasta.DayOfWeek]);
            var ajeno = HabitoHistorico.Crear(otro, "Ajeno", null, desde, [desde.DayOfWeek, hasta.DayOfWeek]);

            await using (var db = CreateContext(database.ConnectionString))
            {
                db.AddRange(propietario, otro);
                await db.SaveChangesAsync();
                var habitos = new HabitoRepository(db);
                await habitos.AgregarAsync(propio, CancellationToken.None);
                await habitos.AgregarAsync(ajeno, CancellationToken.None);
                db.AddRange(new Cumplimiento(propio, desde), new Cumplimiento(propio, hasta),
                    new Cumplimiento(propio, desde.AddDays(-1)), new Cumplimiento(propio, hasta.AddDays(1)),
                    new Cumplimiento(ajeno, desde));
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

            var uno = await LeerAsync(client, "2026-10-30", "2026-10-30");
            Assert.Equal(new DiaCalendarioResponse(desde, 1, 1), Assert.Single(uno));
            var cantidadCorta = consultas.Comandos.Count;
            Assert.Single(consultas.Comandos, EsLecturaCumplimientos);
            consultas.Comandos.Clear();

            var rango = await LeerAsync(client, "2026-10-30", "2026-12-10");
            Assert.Equal(42, rango.Length);
            Assert.Equal(new DiaCalendarioResponse(desde, 1, 1), rango[0]);
            Assert.Equal(new DiaCalendarioResponse(desde.AddDays(1), 0, 0), rango[1]);
            Assert.Equal(new DiaCalendarioResponse(hasta, 1, 1), rango[^1]);
            Assert.Equal(2, rango.Sum(dia => dia.Cumplidos));
            Assert.Equal(cantidadCorta, consultas.Comandos.Count);
            var consulta = Assert.Single(consultas.Comandos, EsLecturaCumplimientos);

            output.WriteLine("Rango de 1 día: {0} lecturas EF. Rango de 42 días: {1}. Una lectura de cumplimientos por rango.",
                cantidadCorta, consultas.Comandos.Count);
            output.WriteLine(consulta);
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    private static bool EsLecturaCumplimientos(string sql) =>
        sql.Contains("[Cumplimiento]", StringComparison.OrdinalIgnoreCase);

    private static async Task<DiaCalendarioResponse[]> LeerAsync(HttpClient client, string desde, string hasta)
    {
        using var respuesta = await client.GetAsync($"/api/habitos/calendario?desde={desde}&hasta={hasta}");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return Assert.IsType<DiaCalendarioResponse[]>(await respuesta.Content.ReadFromJsonAsync<DiaCalendarioResponse[]>());
    }
}
