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

    private sealed class HostSql(string connection, ConsultasEjecutadas consultas) : WebApplicationFactory<Program>
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

    private sealed class ConsultasEjecutadas : DbCommandInterceptor
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
