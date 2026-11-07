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

public sealed class GestionHabitosSqlServerTests(ITestOutputHelper output)
{
    [SqlServerIntegrationFact]
    public async Task ListarYDesactivar_UsanPropietarioYZonaVigente_ConservanHistorial()
    {
        var database = await CreateReadyDatabaseAsync();
        try
        {
            var inicio = new DateOnly(2026,10,8);
            var user = new Usuario("UI005", "ui005@example.invalid", "hash-sintetico", "Etc/UTC");
            var other = new Usuario("Ajeno", "otro@example.invalid", "hash-sintetico", "Etc/UTC");
            var habito = HabitoHistorico.Crear(user,"Lectura legada", "Conservar", inicio,[DayOfWeek.Sunday]);
            var ajeno = HabitoHistorico.Crear(other,"Privado",null,inicio,[DayOfWeek.Monday]);
            await using(var db = CreateContext(database.ConnectionString))
            {
                db.AddRange(user,other); await db.SaveChangesAsync();
                var repo = new HabitoRepository(db);
                await repo.AgregarAsync(habito,default); await repo.AgregarAsync(ajeno,default);
                db.Add(new Cumplimiento(habito,inicio)); await db.SaveChangesAsync();
            }
            using var fixture = new ConsultaDiariaSqlServerTests.HostSql(database.ConnectionString,new());
            using var host = fixture.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.AddSingleton<TimeProvider>(new Reloj())));
            using var client = host.CreateClient(new(){BaseAddress=new("https://localhost")});
            using(var scope=host.Services.CreateScope())
                client.DefaultRequestHeaders.Authorization = new("Bearer",scope.ServiceProvider.GetRequiredService<IAccessTokenService>().Emitir(user.Id,user.Nombre,user.Email).Value);
            var activos=await client.GetFromJsonAsync<HabitoResponse[]>("/api/habitos");
            Assert.Equal(habito.Id, Assert.Single(activos!).Id);
            Assert.Null(activos![0].IconoId);
            Assert.Equal(HttpStatusCode.NotFound,(await client.DeleteAsync($"/api/habitos/{ajeno.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,(await client.PutAsJsonAsync("/api/usuarios/mi-zona-horaria",new{timeZoneId="America/Los_Angeles"})).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,(await client.DeleteAsync($"/api/habitos/{habito.Id}")).StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<HabitoResponse[]>("/api/habitos"))!);
            Assert.Equal(EstadoHabito.Inactivo,(await client.GetFromJsonAsync<HabitoResponse>($"/api/habitos/{habito.Id}"))!.Estado);
            Assert.Equal(HttpStatusCode.NoContent,(await client.DeleteAsync($"/api/habitos/{habito.Id}")).StatusCode);
            await using(var db = CreateContext(database.ConnectionString))
            {
                var persisted=await db.Set<Habito>().Include(h=>h.PeriodosEstado).SingleAsync(h=>h.Id==habito.Id);
                Assert.Equal(2,persisted.PeriodosEstado.Count);
                var inactivo=Assert.Single(persisted.PeriodosEstado,p=>p.Estado==EstadoHabito.Inactivo);
                Assert.Equal(new DateOnly(2026,10,9),inactivo.VigenteDesde);
                Assert.Equal(1,await db.Set<Cumplimiento>().CountAsync());
                Assert.Equal(EstadoHabito.Activo,(await db.Set<Habito>().SingleAsync(h=>h.Id==ajeno.Id)).Estado);
            }
            output.WriteLine("GET propio incluye hábito no programado hoy y legado. DELETE ajeno 404. Mismo token tras cambio de zona: desactivación efectiva 2026-10-09 a las 00:05 UTC del 10/10. Dos períodos, un cumplimiento conservado; ajeno intacto. DELETE repetido 204 sin período adicional.");
        }
        finally { await DropDisposableDatabaseAsync(database); output.WriteLine("Base temporal eliminada por el fixture."); }
    }
    private sealed class Reloj:TimeProvider { public override DateTimeOffset GetUtcNow()=>new(2026,10,10,0,5,0,TimeSpan.Zero); }
}
