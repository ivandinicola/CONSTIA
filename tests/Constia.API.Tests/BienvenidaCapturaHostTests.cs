using System.Net;
using System.Net.Http.Json;
using Constia.Application.Bienvenida;
using Constia.Application.Usuarios;
using Constia.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
namespace Constia.API.Tests;
public sealed class BienvenidaCapturaHostTests
{
    [Fact] public async Task ConfiguracionBienvenidaInvalida_NoImpideArranqueNiLogin()
    {
        using var f=new JwtApiFactory();
        using var h=f.WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Bienvenida:CapturarEventos"]="incorrecto"})));
        using var c=h.CreateClient();
        Assert.False(h.Services.GetRequiredService<OpcionesCapturaBienvenida>().EsValida);
        var r=await c.PostAsJsonAsync("/api/auth/login",new{email=f.Usuario.Email,password=JwtApiFactory.TestPassword});
        Assert.Equal(HttpStatusCode.OK,r.StatusCode);
    }
    [Fact] public async Task FalloCaptura_Conserva503GenericoSinDatosSensibles()
    {
        using var f=new JwtApiFactory();
        using var h=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{s.RemoveAll<IVerificacionCorreoRepository>();s.AddScoped<IVerificacionCorreoRepository,NoDisponible>();}));
        using var c=h.CreateClient();
        var r=await c.PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=Guid.NewGuid(),token=new string('A',43),password="PRIVADO"});
        Assert.Equal(HttpStatusCode.ServiceUnavailable,r.StatusCode);
        var body=await r.Content.ReadAsStringAsync();Assert.Contains("registration_unavailable",body);Assert.DoesNotContain("PRIVADO",body);Assert.DoesNotContain("bienvenida",body,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void FlagsBienvenida_NoRegistranProcesamientoNiProvocanHttp()
    {
        using var f=new JwtApiFactory();
        using var h=f.WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
            ["Bienvenida:CapturarEventos"]="true",["Bienvenida:EpocaCaptura"]="22222222-2222-2222-2222-222222222222",["Bienvenida:EnvioHabilitado"]="true"})));
        using var c=h.CreateClient();
        Assert.True(h.Services.GetRequiredService<OpcionesCapturaBienvenida>().Habilitada);
        Assert.DoesNotContain(h.Services.GetServices<IHostedService>(),s=>s.GetType().Name.Contains("Bienvenida",StringComparison.OrdinalIgnoreCase));
        Assert.Empty(f.CorreosDePrueba);
    }
    private sealed class NoDisponible : IVerificacionCorreoRepository
    {
        public Task<ResultadoConfirmacionCorreo> ConfirmarAsync(Guid id,byte[] hash,string password,PoliticaVerificacionCorreo p,CancellationToken ct)=>throw new CorreoNoDisponibleException();
        public Task<bool> EsquemaDisponibleAsync(CancellationToken ct)=>throw new NotSupportedException();
        public Task<bool> VigenteParaEnvioAsync(Guid id,string email,CancellationToken ct)=>throw new NotSupportedException();
        public Task<ReservaRegistroCorreo> RegistrarAsync(Usuario u,byte[] h,PoliticaVerificacionCorreo p,CancellationToken ct)=>throw new NotSupportedException();
        public Task<EmisionCorreo?> ReenviarAsync(string email,byte[] h,PoliticaVerificacionCorreo p,CancellationToken ct)=>throw new NotSupportedException();
    }
}
