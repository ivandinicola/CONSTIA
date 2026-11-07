using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Constia.API.Contracts;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Habitos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;
using static Constia.API.Tests.RegistrarCumplimientoSqlServerTests;

namespace Constia.API.Tests;

public sealed class ConsultaDiariaSqlServerTests(ITestOutputHelper output)
{
    [SqlServerIntegrationFact]
    public async Task Get_FiltraPropietarioYEstadoHistorico_YLeeCumplimientosEnUnaConsulta()
    {
        var database = await CreateReadyDatabaseAsync();
        try
        {
            var fecha = new DateOnly(2026, 10, 9);
            var propietario = new Usuario("Propietario", "propietario@example.invalid", "hash-de-prueba", "Etc/UTC");
            var otro = new Usuario("Otro", "otro@example.invalid", "hash-de-prueba", "Etc/UTC");
            var historico = HabitoHistorico.Crear(propietario, "Histórico", null, fecha, [fecha.DayOfWeek]);
            historico.Desactivar(fecha.AddDays(1));
            var pendiente = HabitoHistorico.Crear(propietario, "Pendiente", null, fecha.AddDays(-7), [fecha.DayOfWeek]);
            var ajeno = HabitoHistorico.Crear(otro, "Ajeno", null, fecha, [fecha.DayOfWeek]);

            await using (var db = CreateContext(database.ConnectionString))
            {
                db.AddRange(propietario, otro);
                await db.SaveChangesAsync();
                var repositorio = new HabitoRepository(db);
                foreach (var habito in new[] { historico, pendiente, ajeno })
                {
                    await repositorio.AgregarAsync(habito, CancellationToken.None);
                }

                db.AddRange(new Cumplimiento(historico, fecha), new Cumplimiento(ajeno, fecha),
                    new Cumplimiento(pendiente, fecha.AddDays(-7)));
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

            using var response = await client.GetAsync("/api/habitos/diarios?fecha=2026-10-09");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var habitos = Assert.IsType<HabitoDiarioResponse[]>(
                await response.Content.ReadFromJsonAsync<HabitoDiarioResponse[]>());
            Assert.Equal(2, habitos.Length);
            Assert.True(Assert.Single(habitos, h => h.Id == historico.Id).Cumplido);
            Assert.False(Assert.Single(habitos, h => h.Id == pendiente.Id).Cumplido);
            Assert.DoesNotContain(habitos, h => h.Id == ajeno.Id);

            var consulta = Assert.Single(consultas.Comandos,
                sql => sql.Contains("[Cumplimiento]", StringComparison.OrdinalIgnoreCase));
            output.WriteLine("Hábitos devueltos: 2. Lecturas EF: {0}. Lecturas de Cumplimiento: 1.",
                consultas.Comandos.Count);
            output.WriteLine(consulta);
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task GetHoy_UsaZonaPersistidaVigenteYAislaLosHabitosPorPropietario()
    {
        var database = await CreateReadyDatabaseAsync();
        try
        {
            var viernes = new DateOnly(2026, 10, 9);
            var sabado = viernes.AddDays(1);
            var oeste = new Usuario("Oeste", "oeste@example.invalid", "hash-de-prueba", "America/Los_Angeles");
            var este = new Usuario("Este", "este@example.invalid", "hash-de-prueba", "Asia/Tokyo");
            var oesteViernes = HabitoHistorico.Crear(oeste, "Oeste viernes", null, viernes, [DayOfWeek.Friday]);
            var oesteSabado = HabitoHistorico.Crear(oeste, "Oeste sábado", null, viernes, [DayOfWeek.Saturday]);
            var esteViernes = HabitoHistorico.Crear(este, "Este viernes", null, viernes, [DayOfWeek.Friday]);
            var esteSabado = HabitoHistorico.Crear(este, "Este sábado", null, viernes, [DayOfWeek.Saturday]);

            await using (var db = CreateContext(database.ConnectionString))
            {
                db.AddRange(oeste, este);
                await db.SaveChangesAsync();
                var repositorio = new HabitoRepository(db);
                foreach (var habito in new[] { oesteViernes, oesteSabado, esteViernes, esteSabado })
                    await repositorio.AgregarAsync(habito, CancellationToken.None);

                db.Add(new Cumplimiento(oesteViernes, viernes));
                await db.SaveChangesAsync();
            }

            using var fixture = new HostSql(database.ConnectionString, new ConsultasEjecutadas());
            using var host = fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new RelojFijo(
                    new DateTimeOffset(2026, 10, 10, 0, 5, 0, TimeSpan.Zero)))));
            using var clientOeste = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            using var clientEste = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            using (var scope = host.Services.CreateScope())
            {
                var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
                clientOeste.DefaultRequestHeaders.Authorization = new("Bearer",
                    tokens.Emitir(oeste.Id, oeste.Nombre, oeste.Email).Value);
                clientEste.DefaultRequestHeaders.Authorization = new("Bearer",
                    tokens.Emitir(este.Id, este.Nombre, este.Email).Value);
            }

            await VerificarHoy(clientOeste, viernes, oesteViernes.Id, cumplido: true);
            await VerificarHoy(clientEste, sabado, esteSabado.Id, cumplido: false);

            using var cambio = await clientOeste.PutAsJsonAsync("/api/usuarios/mi-zona-horaria",
                new { timeZoneId = "Asia/Tokyo" });
            Assert.Equal(HttpStatusCode.NoContent, cambio.StatusCode);
            await VerificarHoy(clientOeste, sabado, oesteSabado.Id, cumplido: false);
            output.WriteLine("00:05 UTC. Oeste: 2026-10-09, Este: 2026-10-10, Oeste tras el PUT con el mismo Bearer: 2026-10-10.");
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task PostDesdeDosPestanas_PersisteUnaSolaVezYHoyConfirmaElProgreso()
    {
        var database = await CreateReadyDatabaseAsync();
        try
        {
            var fecha = new DateOnly(2026, 10, 10);
            var propietario = new Usuario("UI-009", "ui009@example.invalid", "hash-de-prueba", "Etc/UTC");
            var habito = HabitoHistorico.Crear(propietario, "Leer", null, fecha, [fecha.DayOfWeek]);
            await using (var db = CreateContext(database.ConnectionString))
            {
                db.Add(propietario);
                await db.SaveChangesAsync();
                await new HabitoRepository(db).AgregarAsync(habito, CancellationToken.None);
            }
            using var fixture = new HostSql(database.ConnectionString, new ConsultasEjecutadas());
            using var host = fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new RelojFijo(
                    new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero)))));
            using var primera = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            using var segunda = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            using (var scope = host.Services.CreateScope())
            {
                var token = scope.ServiceProvider.GetRequiredService<IAccessTokenService>()
                    .Emitir(propietario.Id, propietario.Nombre, propietario.Email).Value;
                primera.DefaultRequestHeaders.Authorization = new("Bearer", token);
                segunda.DefaultRequestHeaders.Authorization = new("Bearer", token);
            }
            var respuestas = await Task.WhenAll(
                primera.PostAsJsonAsync($"/api/habitos/{habito.Id}/cumplimientos", new { fecha }),
                segunda.PostAsJsonAsync($"/api/habitos/{habito.Id}/cumplimientos", new { fecha }));
            try
            {
                Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Created);
                Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Conflict);
                await VerificarHoy(primera, fecha, habito.Id, cumplido: true);
                await using var verificacion = CreateContext(database.ConnectionString);
                Assert.Equal(1, await verificacion.Set<Cumplimiento>().CountAsync());
                output.WriteLine("Dos POST concurrentes: 201 y 409. Una fila persistida. GET Hoy confirma 1 de 1.");
            }
            finally
            {
                foreach (var respuesta in respuestas)
                    respuesta.Dispose();
            }
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    private static async Task VerificarHoy(HttpClient client, DateOnly fecha, Guid habitId, bool cumplido)
    {
        using var response = await client.GetAsync("/api/habitos/diarios/hoy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hoy = Assert.IsType<HabitosHoyResponse>(await response.Content.ReadFromJsonAsync<HabitosHoyResponse>());
        Assert.Equal(fecha, hoy.Fecha);
        var habito = Assert.Single(hoy.Habitos);
        Assert.Equal(habitId, habito.Id);
        Assert.Equal(cumplido, habito.Cumplido);
    }

    private sealed class RelojFijo(DateTimeOffset instante) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instante;
    }

    internal sealed class HostSql(string connection, ConsultasEjecutadas consultas) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connection,
                    ["JwtSettings:SigningKey"] = "comp-007-synthetic-test-signing-key-32-bytes",
                    ["JwtSettings:Issuer"] = "constia-comp007",
                    ["JwtSettings:Audience"] = "constia-comp007-tests",
                    ["JwtSettings:ExpirationMinutes"] = "10",
                    ["Resend:Enabled"] = "false",
                    ["VerificacionCorreo:PublicRegistrationEnabled"] = "false",
                    ["VerificacionCorreo:C2Enabled"] = "false"
                }));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
                services.AddDbContext<ConstiaDbContext>(options => options.AddInterceptors(consultas)));
        }
    }

    internal sealed class ConsultasEjecutadas : DbCommandInterceptor
    {
        public List<string> Comandos { get; } = [];

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result)
        {
            Comandos.Add(command.CommandText);
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Comandos.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
