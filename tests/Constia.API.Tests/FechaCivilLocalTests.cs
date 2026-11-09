using System.Net;
using System.Net.Http.Json;
using Constia.API.Contracts;
using Constia.Application.Usuarios;
using Constia.Domain;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
namespace Constia.API.Tests;
public sealed class FechaCivilLocalTests
{
    [Fact]
    public async Task SinAutenticacion_401()
    { using var f=new JwtApiFactory();using var c=f.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/usuarios/mi-fecha-local")).StatusCode); }
    [Fact]
    public async Task ZonaVigente_MismoToken_IndependienteDeHistorialYAislada()
    {
        using var f=new JwtApiFactory(); f.HistorialCompleto=false;f.SnapshotCapturado=false;
        f.Usuario.CambiarZonaHoraria("America/Los_Angeles");
        var otro=new Usuario("Otro","otro@example.invalid","hash","Asia/Tokyo");f.UsuariosPersistidos[otro.Id]=otro;
        using var host=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.AddSingleton<TimeProvider>(new Reloj())));
        using var c=host.CreateClient(new(){BaseAddress=new("https://localhost")});
        string Token(Usuario u){using var scope=host.Services.CreateScope();return scope.ServiceProvider.GetRequiredService<IAccessTokenService>().Emitir(u.Id,u.Nombre,u.Email).Value;}
        c.DefaultRequestHeaders.Authorization=new("Bearer",Token(f.Usuario));
        using var r=await c.GetAsync("/api/usuarios/mi-fecha-local");Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Contains("no-store",r.Headers.CacheControl!.ToString());
        Assert.Equal(new DateOnly(2026,11,7),(await r.Content.ReadFromJsonAsync<FechaDto>())!.Fecha);
        f.Usuario.CambiarZonaHoraria("Asia/Tokyo");
        Assert.Equal(new DateOnly(2026,11,8),(await c.GetFromJsonAsync<FechaDto>("/api/usuarios/mi-fecha-local"))!.Fecha);
        f.Usuario.CambiarZonaHoraria("America/Los_Angeles");c.DefaultRequestHeaders.Authorization=new("Bearer",Token(otro));
        Assert.Equal(new DateOnly(2026,11,8),(await c.GetFromJsonAsync<FechaDto>("/api/usuarios/mi-fecha-local"))!.Fecha);
        f.UsuariosPersistidos.TryRemove(otro.Id,out _);Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/usuarios/mi-fecha-local")).StatusCode);
    }
    private sealed record FechaDto(DateOnly Fecha);
    private sealed class Reloj:TimeProvider{public override DateTimeOffset GetUtcNow()=>new(2026,11,8,0,5,0,TimeSpan.Zero);}
}
