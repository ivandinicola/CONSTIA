using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Constia.Application.Habitos;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure.Habitos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Constia.API.Tests.ConsultaDiariaSqlServerTests;
using static Constia.API.Tests.RegistrarCumplimientoSqlServerTests;

namespace Constia.API.Tests;

public sealed class HistorialHabitoSqlServerTests
{
    [SqlServerIntegrationFact]
    public async Task HistorialPorHabito_AislaPropiedadYConservaHechosIndependientes()
    {
        var database = await CreateReadyDatabaseAsync();
        try
        {
            var lunes = new DateOnly(2026, 11, 2);
            var usuario = new Usuario("Propietario", "ui011@example.invalid", "hash", "Etc/UTC");
            var otro = new Usuario("Otro", "ui011-ajeno@example.invalid", "hash", "Etc/UTC");
            var propio = HabitoHistorico.Crear(usuario, "Leer", null, lunes, [DayOfWeek.Monday, DayOfWeek.Tuesday]);
            propio.ActualizarConfiguracionVersionada("Leer", null, [DayOfWeek.Thursday, DayOfWeek.Friday], lunes.AddDays(2));
            propio.Desactivar(lunes.AddDays(4));
            var legado = HabitoHistorico.Crear(usuario, "Paseo breve", null, lunes, [DayOfWeek.Monday]);
            var ajeno = HabitoHistorico.Crear(otro, "Ajeno", null, lunes, [DayOfWeek.Monday]);

            await using (var db = CreateContext(database.ConnectionString))
            {
                db.AddRange(usuario, otro);
                await db.SaveChangesAsync();
                var repositorio = new HabitoRepository(db);
                foreach (var habito in new[] { propio, legado, ajeno })
                    await repositorio.AgregarAsync(habito, default);
                db.RemoveRange(legado.PeriodosEstado);
                db.AddRange(new Cumplimiento(propio, lunes), new Cumplimiento(propio, lunes.AddDays(2)),
                    new Cumplimiento(propio, lunes.AddDays(4)), new Cumplimiento(legado, lunes), new Cumplimiento(ajeno, lunes));
                await db.SaveChangesAsync();
            }

            var consultas = new ConsultasEjecutadas();
            using var host = new HostSql(database.ConnectionString, consultas);
            using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            string Url(Guid id, DateOnly desde, DateOnly hasta) =>
                $"/api/habitos/{id}/historial?desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}";
            var corta = Url(propio.Id, lunes, lunes);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(corta)).StatusCode);
            using (var scope = host.Services.CreateScope())
            {
                var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                    tokens.Emitir(usuario.Id, usuario.Nombre, usuario.Email).Value);
            }

            foreach (var id in new[] { ajeno.Id, Guid.NewGuid() })
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(id, lunes, lunes))).StatusCode);
            foreach (var url in new[] { Url(propio.Id, lunes, lunes.AddDays(42)), Url(propio.Id, lunes, lunes.AddDays(-1)),
                $"/api/habitos/{propio.Id}/historial?desde=2026-1-02&hasta=2026-11-02", $"/api/habitos/{propio.Id}/historial" })
                Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url)).StatusCode);

            consultas.Comandos.Clear();
            var uno = (await client.GetFromJsonAsync<DiaHistorialHabito[]>(corta))!;
            Assert.Equal(new(lunes, "cumplido", true), Assert.Single(uno));
            var lecturasCortas = consultas.Comandos.Count;
            Assert.Single(consultas.Comandos, LeeCumplimientos);
            consultas.Comandos.Clear();

            var rango = (await client.GetFromJsonAsync<DiaHistorialHabito[]>(Url(propio.Id, lunes, lunes.AddDays(41))))!;
            Assert.Equal(Enumerable.Range(0, 42).Select(lunes.AddDays), rango.Select(d => d.Fecha));
            Assert.Equal(new(lunes.AddDays(1), "pendiente", false), rango[1]);
            Assert.Equal(new(lunes.AddDays(2), "no_programado", true), rango[2]);
            Assert.Equal(new(lunes.AddDays(3), "pendiente", false), rango[3]);
            Assert.Equal(new(lunes.AddDays(4), "no_programado", true), rango[4]);
            Assert.Equal(lecturasCortas, consultas.Comandos.Count);
            var lectura = Assert.Single(consultas.Comandos, LeeCumplimientos);
            Assert.Contains("[HabitoId]", lectura);
            Assert.Contains("[UsuarioId]", lectura);

            var desconocido = (await client.GetFromJsonAsync<DiaHistorialHabito[]>(Url(legado.Id, lunes, lunes)))!;
            Assert.Equal(new(lunes, "desconocido", true), Assert.Single(desconocido));
            Assert.Equal(HttpStatusCode.UnprocessableEntity,
                (await client.GetAsync($"/api/habitos/calendario?desde={lunes:yyyy-MM-dd}&hasta={lunes:yyyy-MM-dd}")).StatusCode);

            await using (var db = CreateContext(database.ConnectionString))
                await db.Set<InicializacionEstadoHabito>().ExecuteDeleteAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(corta)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(ajeno.Id, lunes, lunes))).StatusCode);
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    private static bool LeeCumplimientos(string sql) => sql.Contains("[Cumplimiento]", StringComparison.OrdinalIgnoreCase);
}
