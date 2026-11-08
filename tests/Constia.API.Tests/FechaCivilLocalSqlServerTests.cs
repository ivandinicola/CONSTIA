using System.Net;
using System.Net.Http.Json;
using Constia.API.Contracts;
using Constia.Application.Usuarios;
using Constia.Domain;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Constia.API.Tests.RegistrarCumplimientoSqlServerTests;
namespace Constia.API.Tests;
public sealed class FechaCivilLocalSqlServerTests
{
    [SqlServerIntegrationFact]
    public async Task FechaLocal_UsaZonaPersistidaVigente_ConMismoToken()
    {
        var database=await CreateReadyDatabaseAsync();
        try
        {
            var u=new Usuario("UI010","ui010@example.invalid","hash","America/Los_Angeles");var otro=new Usuario("Otro","otro010@example.invalid","hash","Asia/Tokyo");
            await using(var db=CreateContext(database.ConnectionString)){db.AddRange(u,otro);await db.SaveChangesAsync();}
            using var f=new ConsultaDiariaSqlServerTests.HostSql(database.ConnectionString,new());
            using var host=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.AddSingleton<TimeProvider>(new Reloj())));
            using var c=host.CreateClient(new(){BaseAddress=new("https://localhost")});
            string Token(Usuario x){using var scope=host.Services.CreateScope();return scope.ServiceProvider.GetRequiredService<IAccessTokenService>().Emitir(x.Id,x.Nombre,x.Email).Value;}
            c.DefaultRequestHeaders.Authorization=new("Bearer",Token(u));
            Assert.Equal(new DateOnly(2026,11,7),(await c.GetFromJsonAsync<FechaCivilLocalResponse>("/api/usuarios/mi-fecha-local"))!.Fecha);
            Assert.Equal(HttpStatusCode.NoContent,(await c.PutAsJsonAsync("/api/usuarios/mi-zona-horaria",new{timeZoneId="Asia/Tokyo"})).StatusCode);
            Assert.Equal(new DateOnly(2026,11,8),(await c.GetFromJsonAsync<FechaCivilLocalResponse>("/api/usuarios/mi-fecha-local"))!.Fecha);
            c.DefaultRequestHeaders.Authorization=new("Bearer",Token(otro));
            Assert.Equal(new DateOnly(2026,11,8),(await c.GetFromJsonAsync<FechaCivilLocalResponse>("/api/usuarios/mi-fecha-local"))!.Fecha);
        }
        finally {await DropDisposableDatabaseAsync(database);}
    }
    private sealed class Reloj:TimeProvider{public override DateTimeOffset GetUtcNow()=>new(2026,11,8,0,5,0,TimeSpan.Zero);}
}
