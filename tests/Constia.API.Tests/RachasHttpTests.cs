using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Constia.API.Contracts;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure.Habitos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Constia.API.Tests.ConsultaDiariaSqlServerTests;
using static Constia.API.Tests.RegistrarCumplimientoSqlServerTests;

namespace Constia.API.Tests;

public sealed class RachasHttpTests
{
    [Fact]
    public async Task Consultas_RespetanAutenticacionPropiedadYDisponibilidad()
    {
        using var host = new JwtApiFactory();
        using var client = host.CreateClient();
        var fecha = new DateOnly(2026, 10, 7);
        var propio = HabitoHistorico.Crear(host.Usuario, "Propio", null, fecha, [fecha.DayOfWeek]);
        var otro = new Usuario("Otro", "ajeno-rachas@example.invalid", "hash", "Etc/UTC");
        var ajeno = HabitoHistorico.Crear(otro, "Ajeno", null, fecha, [fecha.DayOfWeek]);
        var general = "/api/habitos/rachas";
        string Individual(Guid id) => $"/api/habitos/{id}/rachas";

        foreach (var url in new[] { general, Individual(propio.Id) })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
        Autenticar(client, host.Services, host.Usuario);

        host.ReemplazarHabitos([ajeno]);
        var cero = new MetricaRachaResponse("disponible", 0);
        Assert.Equal(new(new DateOnly(2026, 10, 10), cero, cero),
            await client.GetFromJsonAsync<RachasResponse>(general));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Individual(ajeno.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Individual(Guid.NewGuid()))).StatusCode);

        host.ReemplazarHabitos([propio, ajeno]);
        QuitarPeriodos(propio);
        host.CumplimientosPersistidos[(propio.Id, fecha)] = new Cumplimiento(propio, fecha);

        foreach (var url in new[] { general, Individual(propio.Id) })
            await VerificarProblema(client, url, HttpStatusCode.UnprocessableEntity, "habit_status_history_unknown");

        host.HistorialCompleto = false;
        foreach (var url in new[] { general, Individual(propio.Id) })
            await VerificarProblema(client, url, HttpStatusCode.ServiceUnavailable, "habit_status_history_not_initialized");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Individual(ajeno.Id))).StatusCode);
    }

    [Fact]
    public async Task Consultas_Devuelven200ConUnaMetricaDeterminable()
    {
        using var host = new JwtApiFactory();
        using var client = host.CreateClient();
        Autenticar(client, host.Services, host.Usuario);
        var inicio = new DateOnly(2026, 10, 6);
        var legado = HabitoHistorico.Crear(host.Usuario, "Legado", null, inicio, Enum.GetValues<DayOfWeek>());
        QuitarPeriodos(legado);
        legado.InicializarHistorialEstadoDesdeCorte(inicio.AddDays(2), EstadoHabito.Activo);
        host.ReemplazarHabitos([legado]);
        foreach (var fecha in new[] { inicio, inicio.AddDays(1), inicio.AddDays(3) })
            host.CumplimientosPersistidos[(legado.Id, fecha)] = new Cumplimiento(legado, fecha);

        var esperado = new RachasResponse(inicio.AddDays(4), new("disponible", 1), new("no_disponible", null));
        Assert.Equal(esperado, await client.GetFromJsonAsync<RachasResponse>("/api/habitos/rachas"));
        Assert.Equal(esperado, await client.GetFromJsonAsync<RachasResponse>($"/api/habitos/{legado.Id}/rachas"));
    }

    private static void QuitarPeriodos(Habito habito)
    {
        var periodos = (List<PeriodoEstadoHabito>)typeof(Habito)
            .GetField("_periodosEstado", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(habito)!;
        periodos.Clear();
    }

    internal static void Autenticar(HttpClient client, IServiceProvider services, Usuario usuario)
    {
        using var scope = services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            tokens.Emitir(usuario.Id, usuario.Nombre, usuario.Email).Value);
    }

    private static async Task VerificarProblema(HttpClient client, string url, HttpStatusCode status, string codigo)
    {
        using var respuesta = await client.GetAsync(url);
        Assert.Equal(status, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var problema = await respuesta.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(codigo, problema!.Extensions["code"]?.ToString());
    }
}

public sealed class RachasSqlServerTests(ITestOutputHelper output)
{
    [SqlServerIntegrationFact]
    public async Task Consultas_UsanZonaVigenteHistorialCompletoYLecturasEnBloque()
    {
        var database = await CreateReadyDatabaseAsync();
        try
        {
            var inicio = new DateOnly(2026, 9, 1);
            var cambio = new DateOnly(2026, 10, 31);
            var desactivacion = new DateOnly(2026, 11, 3);
            var corte = new DateOnly(2026, 11, 4);
            var manana = corte.AddDays(1);
            var propietario = new Usuario("Propietario", "rachas-propio@example.invalid", "hash", "America/Los_Angeles");
            var otro = new Usuario("Otro", "rachas-ajeno@example.invalid", "hash", "Asia/Tokyo");
            var largo = HabitoHistorico.Crear(propietario, "Secuencia larga", null, inicio, Enum.GetValues<DayOfWeek>());
            largo.ActualizarConfiguracionVersionada("Secuencia larga", null, [DayOfWeek.Monday, DayOfWeek.Tuesday], cambio);
            largo.Desactivar(desactivacion);
            var legado = HabitoHistorico.Crear(propietario, "Legado", null, inicio.AddDays(-1), [DayOfWeek.Monday]);
            legado.Desactivar(inicio);
            var reciente = HabitoHistorico.Crear(propietario, "Reciente", null, corte, [corte.DayOfWeek, manana.DayOfWeek]);
            var ajeno = HabitoHistorico.Crear(otro, "Ajeno", null, manana, [manana.DayOfWeek]);

            await using (var db = CreateContext(database.ConnectionString))
            {
                db.AddRange(propietario, otro);
                await db.SaveChangesAsync();
                var repo = new HabitoRepository(db);
                foreach (var habito in new[] { largo, legado, reciente, ajeno })
                    await repo.AgregarAsync(habito, default);

                db.Remove(legado.PeriodosEstado.Single(periodo => periodo.Estado == EstadoHabito.Activo));
                db.AddRange(Enumerable.Range(0, 60).Select(i => new Cumplimiento(largo, inicio.AddDays(i))));
                db.AddRange(new Cumplimiento(largo, cambio), new Cumplimiento(largo, desactivacion),
                    new Cumplimiento(legado, inicio.AddDays(-1)), new Cumplimiento(reciente, corte),
                    new Cumplimiento(reciente, manana), new Cumplimiento(ajeno, manana));
                await db.SaveChangesAsync();
            }

            var consultas = new ConsultasEjecutadas();
            using var fixture = new HostSql(database.ConnectionString, consultas);
            using var host = fixture.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new Reloj())));
            using var client = host.CreateClient(new() { BaseAddress = new("https://localhost") });
            var general = "/api/habitos/rachas";
            string Individual(Guid id) => $"/api/habitos/{id}/rachas";
            var uno = new MetricaRachaResponse("disponible", 1);
            var dos = new MetricaRachaResponse("disponible", 2);
            var sinDato = new MetricaRachaResponse("no_disponible", null);

            RachasHttpTests.Autenticar(client, host.Services, otro);
            consultas.Comandos.Clear();
            Assert.Equal(new(manana, uno, uno), await client.GetFromJsonAsync<RachasResponse>(general));
            var lecturasCortas = consultas.Comandos.Count;
            Assert.Single(consultas.Comandos, LeeCumplimientos);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Individual(largo.Id))).StatusCode);

            RachasHttpTests.Autenticar(client, host.Services, propietario);
            consultas.Comandos.Clear();
            Assert.Equal(new(corte, uno, sinDato), await client.GetFromJsonAsync<RachasResponse>(general));
            Assert.Equal(lecturasCortas, consultas.Comandos.Count);
            var sqlGeneral = Assert.Single(consultas.Comandos, LeeCumplimientos);
            Assert.Contains("[UsuarioId]", sqlGeneral);
            Assert.Contains("[Fecha]", sqlGeneral);
            output.WriteLine($"General: {lecturasCortas} lecturas para 1 hábito y 1 día, y {consultas.Comandos.Count} para 3 hábitos y 66 días; una lectura de cumplimientos en cada consulta.");

            consultas.Comandos.Clear();
            using (var respuesta = await client.GetAsync(Individual(largo.Id)))
            {
                Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
                var cuerpo = await respuesta.Content.ReadAsStringAsync();
                Assert.Equal(new(corte, new("no_aplicable", null), new("disponible", 60)),
                    JsonSerializer.Deserialize<RachasResponse>(cuerpo, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                using var json = JsonDocument.Parse(cuerpo);
                Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("actual").GetProperty("valor").ValueKind);
            }
            var lecturasIndividual = consultas.Comandos.Count;
            var sqlIndividual = Assert.Single(consultas.Comandos, LeeCumplimientos);
            Assert.Contains("[HabitoId]", sqlIndividual);
            Assert.Contains("[UsuarioId]", sqlIndividual);

            consultas.Comandos.Clear();
            Assert.Equal(new(corte, uno, uno), await client.GetFromJsonAsync<RachasResponse>(Individual(reciente.Id)));
            Assert.Equal(lecturasIndividual, consultas.Comandos.Count);
            Assert.Single(consultas.Comandos, LeeCumplimientos);
            output.WriteLine($"Individual: {lecturasIndividual} lecturas para el hábito largo y {consultas.Comandos.Count} para el reciente; una lectura de cumplimientos por hábito consultado.");

            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                "/api/usuarios/mi-zona-horaria", new { timeZoneId = "Asia/Tokyo" })).StatusCode);
            Assert.Equal(new(manana, dos, sinDato), await client.GetFromJsonAsync<RachasResponse>(general));
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    private static bool LeeCumplimientos(string sql) => sql.Contains("[Cumplimiento]", StringComparison.OrdinalIgnoreCase);

    private sealed class Reloj : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 5, 0, 5, 0, TimeSpan.Zero);
    }
}
