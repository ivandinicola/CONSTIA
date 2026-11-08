using Constia.Application.Habitos;
using System.Net;
using System.Net.Http.Json;
using Constia.API.Contracts;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure.Habitos;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Constia.API.Tests.RegistrarCumplimientoSqlServerTests;

namespace Constia.API.Tests;

public sealed class CrearHabitoWebSqlServerTests(ITestOutputHelper output)
{
    [SqlServerIntegrationFact]
    public async Task AltaHttp_UsaZonaVigenteConMismoToken_PersisteInicioYDiasSinDuplicados()
    {
        var database = await CreateReadyDatabaseAsync();
        try
        {
            var user = new Usuario("UI006", "ui006@example.invalid", "hash-sintetico", "Etc/UTC");
            var otro = new Usuario("Otro", "otro-ui006@example.invalid", "hash-sintetico", "Pacific/Auckland");
            await using (var db = CreateContext(database.ConnectionString))
            { db.AddRange(user, otro); await db.SaveChangesAsync(); }
            using var fixture = new ConsultaDiariaSqlServerTests.HostSql(database.ConnectionString, new());
            using var host = fixture.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new Reloj())));
            using var client = host.CreateClient(new() { BaseAddress = new("https://localhost") });
            using (var scope = host.Services.CreateScope())
                client.DefaultRequestHeaders.Authorization = new("Bearer", scope.ServiceProvider.GetRequiredService<IAccessTokenService>().Emitir(user.Id, user.Nombre, user.Email).Value);
            var categorias = await client.GetFromJsonAsync<CategoriaHabitoListado[]>("/api/habitos/categorias");
            var iconos = await client.GetFromJsonAsync<IconoHabitoListado[]>("/api/habitos/iconos");
            Assert.Equal(10, categorias!.Length); Assert.Equal(16, iconos!.Length);
            var categoria = categorias.Single(c => c.Nombre == "Aprendizaje y crecimiento").Id;
            using var first = await client.PostAsJsonAsync("/api/habitos", new { nombre = "Nombre de más de cincuenta caracteres para no inventar una restricción de interfaz", categoriaId = categoria, iconoId = "billete", diasProgramados = new[] { 1, 0, 1 } });
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var antes = (await first.Content.ReadFromJsonAsync<HabitoResponse>())!;
            Assert.Equal(new DateOnly(2026, 11, 8), antes.StartDate);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/usuarios/mi-zona-horaria", new { timeZoneId = "America/Los_Angeles" })).StatusCode);
            using var second = await client.PostAsJsonAsync("/api/habitos", new { nombre = "Sin descripción", categoriaId = categoria, iconoId = "libro", diasProgramados = new[] { 2, 4, 2 } });
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
            var despues = (await second.Content.ReadFromJsonAsync<HabitoResponse>())!;
            Assert.Equal(new DateOnly(2026, 11, 7), despues.StartDate);
            Assert.Null(despues.Descripcion);
            Assert.Equal(new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday }, despues.DiasProgramados);
            await using var verify = CreateContext(database.ConnectionString);
            var saved = await new HabitoRepository(verify).BuscarPorIdYUsuarioAsync(despues.Id, user.Id, default);
            Assert.NotNull(saved); Assert.Null(await new HabitoRepository(verify).BuscarPorIdYUsuarioAsync(despues.Id, otro.Id, default));
            Assert.Equal(new DateOnly(2026, 11, 7), saved.FechaInicio);
            Assert.Equal(new DateOnly(2026, 11, 7), Assert.Single(saved.Programaciones).VigenteDesde);
            Assert.Equal(new DateOnly(2026, 11, 7), Assert.Single(saved.PeriodosEstado).VigenteDesde);
            Assert.Equal(2, saved.DiasProgramados.Count); Assert.DoesNotContain(saved.DiasProgramados, d => d.Dia == DayOfWeek.Saturday);
            Assert.Equal(2, await verify.Habitos.CountAsync());
            output.WriteLine("Host HTTP + EF real: catálogos 10/16; 00:05 UTC -> 08/11 UTC; mismo token y zona vigente Los Ángeles -> 07/11. Inicio no programado permitido. Días únicos y vigencias persistidas. Propiedad aislada. Nombre >50 válido.");
        }
        finally { await DropDisposableDatabaseAsync(database); output.WriteLine("Base temporal eliminada por fixture."); }
    }
    private sealed class Reloj : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026,11,8,0,5,0,TimeSpan.Zero); }
}
