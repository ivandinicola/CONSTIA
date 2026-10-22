using Constia.Application.Correo;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Constia.API.Tests;

public sealed class VerificacionCorreoHttpTests
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HttpClient,ICanalVerificacionCorreo> Canales = new();
    private static HttpClient Cliente(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        Canales.Add(client,host.Services.GetRequiredService<ICanalVerificacionCorreo>());
        return client;
    }
    private static async Task Drenar(HttpClient client)
    {
        if (!Canales.TryGetValue(client,out var canal)) throw new InvalidOperationException("Cliente sin canal de prueba.");
        using var limite=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await canal.EsperarVacioAsync(limite.Token);
    }
    private const string Password = "ABCDE1.@";

    private static WebApplicationFactory<Program> Host(JwtApiFactory f, Dictionary<string,string?>? settings = null, TimeProvider? clock = null, bool production = false, bool noDelivery = false) =>
        f.WithWebHostBuilder(b =>
        {
            if(production) b.UseEnvironment("Production");
            if(settings is not null) b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(settings));
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IUsuarioPasswordHasher>(); s.AddScoped<IUsuarioPasswordHasher,AspNetCorePasswordHasher>();
                if(clock is not null) { s.RemoveAll<TimeProvider>(); s.AddSingleton(clock); }
                if(noDelivery)
                {
                    s.RemoveAll<ICanalVerificacionCorreo>();
                    s.AddSingleton<ICanalVerificacionCorreo>(_ => { var c = new Constia.Infrastructure.Correo.ColaVerificacionCorreoMemoria(new()); c.Fallar(); return c; });
                }
            });
        });

    private static async Task<(Guid UserId,Guid DesafioId,string Token,string Email)> Registrar(JwtApiFactory f,HttpClient client)
    {
        var email = $"{Guid.NewGuid()}@example.invalid";
        var r = await client.PostAsJsonAsync("/api/usuarios",new { nombre="Ana",email,password=Password,timeZoneId="Etc/UTC" });
        Assert.Equal(HttpStatusCode.Created,r.StatusCode);
        using var body = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal(new[]{"email","id","nombre"},body.RootElement.EnumerateObject().Select(x=>x.Name).Order().ToArray());
        var u = body.RootElement.GetProperty("id").GetGuid();
        await Drenar(client);
        var mail = Assert.Single(f.CorreosDePrueba,x=>x.Email==email);
        Assert.Equal(EstadoVerificacionCorreo.Pendiente,f.UsuariosPersistidos[u].EstadoVerificacionCorreo);
        Assert.False(HashTokenVerificacion.Coincide(new byte[32],f.DesafiosCorreo[mail.Id].TokenHash));
        return(u,mail.Id,mail.Token,email);
    }

    [Fact]
    public async Task RegistroCerrado_Devuelve503SinCrearUsuario()
    {
        using var f = new JwtApiFactory();
        using var host = Host(f,new(){["VerificacionCorreo:PublicRegistrationEnabled"]="false"});
        var email = $"{Guid.NewGuid()}@example.invalid";
        var r = await Cliente(host).PostAsJsonAsync("/api/usuarios",new { nombre="Ana",email,password=Password,timeZoneId="Etc/UTC" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable,r.StatusCode);
        Assert.Contains("registration_unavailable",await r.Content.ReadAsStringAsync());
        Assert.DoesNotContain(f.UsuariosPersistidos.Values,x=>x.Email==email);
        Assert.Empty(f.DesafiosCorreo);
    }

    [Theory]
    [InlineData("http://constia.example")]
    [InlineData("")]
    [InlineData("https://usuario:clave@constia.example")]
    public async Task UrlInsegura_CierraRegistro(string url)
    {
        using var f = new JwtApiFactory();
        using var host = Host(f,new(){["VerificacionCorreo:PublicUrl"]=url});
        var r = await Cliente(host).PostAsJsonAsync("/api/usuarios",new { nombre="Ana",email="nuevo@example.invalid",password=Password,timeZoneId="Etc/UTC" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable,r.StatusCode);
        Assert.Empty(f.DesafiosCorreo);
    }

    [Theory]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task SinCanalOConCapturadorEnProduccion_NoHabilitaAltas(bool noDelivery,bool production)
    {
        using var f = new JwtApiFactory();
        using var host = Host(f,production:production,noDelivery:noDelivery);
        var r = await Cliente(host).PostAsJsonAsync("/api/usuarios",new { nombre="Ana",email="nuevo@example.invalid",password=Password,timeZoneId="Etc/UTC" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable,r.StatusCode);
        Assert.Empty(f.DesafiosCorreo);
    }

    [Fact]
    public async Task EsquemaAusente_CierraAntesDePersistir()
    {
        using var f = new JwtApiFactory { EsquemaCorreoDisponible=false };
        using var host=Host(f);
        var r=await Cliente(host).PostAsJsonAsync("/api/usuarios",new{nombre="Ana",email="nuevo@example.invalid",password=Password,timeZoneId="Etc/UTC"});
        Assert.Equal(HttpStatusCode.ServiceUnavailable,r.StatusCode);
        Assert.Empty(f.DesafiosCorreo);
    }

    [Fact]
    public async Task ConfirmacionDesconocida_ErrorGenericoSinSecretos()
    {
        using var f=new JwtApiFactory(); using var host=Host(f);
        var r=await Cliente(host).PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=Guid.NewGuid(),token=new string('A',43),password="historica"});
        Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
        var text=await r.Content.ReadAsStringAsync();
        Assert.Contains("email_verification_invalid",text);
        Assert.DoesNotContain("historica",text);
    }

    [Fact]
    public async Task Pendiente_LoginValidaCredencialesAntesDe403_SinJwt()
    {
        using var f=new JwtApiFactory(); using var host=Host(f); using var c=Cliente(host);
        var u=await Registrar(f,c);
        var wrong=await c.PostAsJsonAsync("/api/auth/login",new{email=u.Email,password="incorrecta"});
        Assert.Equal(HttpStatusCode.Unauthorized,wrong.StatusCode);
        Assert.DoesNotContain("email_verification_required",await wrong.Content.ReadAsStringAsync());
        var right=await c.PostAsJsonAsync("/api/auth/login",new{email=u.Email,password=Password});
        Assert.Equal(HttpStatusCode.Forbidden,right.StatusCode);
        var body=await right.Content.ReadAsStringAsync();
        Assert.Contains("email_verification_required",body);
        Assert.DoesNotContain("accessToken",body);
    }

    [Fact]
    public async Task TokenOPasswordIncorrectos_RespuestaIgual_NoConsumenLuegoConfirma204()
    {
        using var f=new JwtApiFactory(); using var host=Host(f); using var c=Cliente(host);
        var u=await Registrar(f,c);
        var distinto=(u.Token[0]=='A'?"B":"A")+u.Token[1..];
        var tokenMal=await c.PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=u.DesafioId,token=distinto,password=Password});
        var claveMal=await c.PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=u.DesafioId,token=u.Token,password="incorrecta"});
        Assert.Equal(HttpStatusCode.BadRequest,tokenMal.StatusCode); Assert.Equal(HttpStatusCode.BadRequest,claveMal.StatusCode);
        using var a=JsonDocument.Parse(await tokenMal.Content.ReadAsStringAsync());
        using var b=JsonDocument.Parse(await claveMal.Content.ReadAsStringAsync());
        Assert.Equal(a.RootElement.GetProperty("code").GetString(),b.RootElement.GetProperty("code").GetString());
        Assert.Equal(a.RootElement.GetProperty("detail").GetString(),b.RootElement.GetProperty("detail").GetString());
        Assert.Null(f.DesafiosCorreo[u.DesafioId].ConsumidoEnUtc);
        Assert.Equal(EstadoVerificacionCorreo.Pendiente,f.UsuariosPersistidos[u.UserId].EstadoVerificacionCorreo);
        var ok=await c.PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=u.DesafioId,token=u.Token,password=Password});
        Assert.Equal(HttpStatusCode.NoContent,ok.StatusCode); Assert.Empty(await ok.Content.ReadAsByteArrayAsync());
        Assert.Equal(EstadoVerificacionCorreo.Verificado,f.UsuariosPersistidos[u.UserId].EstadoVerificacionCorreo);
        Assert.NotNull(f.DesafiosCorreo[u.DesafioId].ConsumidoEnUtc);
        var used=await c.PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=u.DesafioId,token=u.Token,password=Password});
        Assert.Equal(HttpStatusCode.BadRequest,used.StatusCode);
        var login=await c.PostAsJsonAsync("/api/auth/login",new{email=u.Email,password=Password});
        Assert.Equal(HttpStatusCode.OK,login.StatusCode);
    }

    [Fact]
    public async Task Confirmacion_NoReaplicaPoliticaDeAltas_NiModificaHash()
    {
        using var f=new JwtApiFactory(); using var host=Host(f);
        var hash=new AspNetCorePasswordHasher().HashPassword("vieja");
        var user=new Usuario("Historico pendiente","pendiente@example.invalid",hash,"Etc/UTC");
        var token=new GeneradorTokenVerificacion().Generar();
        var d=new DesafioVerificacionCorreo(user.Id,user.Email,token.Hash,DateTimeOffset.Parse("2026-10-10T02:30:00Z"));
        f.UsuariosPersistidos[user.Id]=user; f.DesafiosCorreo[d.Id]=d;
        var r=await Cliente(host).PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=d.Id,token=token.Valor,password="vieja"});
        Assert.Equal(HttpStatusCode.NoContent,r.StatusCode);
        Assert.Equal(hash,user.PasswordHash);
    }

    [Fact]
    public async Task Reenvio_ReemplazaDespuesDe60Segundos_AnteriorYaNoSirve()
    {
        using var f=new JwtApiFactory(); var clock=new Clock();
        using var host=Host(f,clock:clock); using var c=Cliente(host);
        var u=await Registrar(f,c);
        Assert.Equal(HttpStatusCode.Accepted,(await c.PostAsJsonAsync("/api/auth/email/reenviar",new{email=u.Email})).StatusCode);
        await Drenar(c);
        Assert.Single(f.DesafiosCorreo);
        clock.Now=clock.Now.AddSeconds(60);
        Assert.Equal(HttpStatusCode.Accepted,(await c.PostAsJsonAsync("/api/auth/email/reenviar",new{email=u.Email})).StatusCode);
        await Drenar(c);
        Assert.Equal(2,f.DesafiosCorreo.Count);
        Assert.NotNull(f.DesafiosCorreo[u.DesafioId].InvalidadoEnUtc);
        var old=await c.PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=u.DesafioId,token=u.Token,password=Password});
        Assert.Equal(HttpStatusCode.BadRequest,old.StatusCode);
        var nuevo=f.CorreosDePrueba.Single(x=>x.Id!=u.DesafioId);
        var ok=await c.PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=nuevo.Id,token=nuevo.Token,password=Password});
        Assert.Equal(HttpStatusCode.NoContent,ok.StatusCode);
    }

    [Fact]
    public async Task Reenvio_InexistenteYVerificado_RespuestaPublicaIgual()
    {
        using var f=new JwtApiFactory(); using var host=Host(f); using var c=Cliente(host);
        var a=await c.PostAsJsonAsync("/api/auth/email/reenviar",new{email="ausente@example.invalid"});
        var b=await c.PostAsJsonAsync("/api/auth/email/reenviar",new{email=f.Usuario.Email});
        Assert.Equal(HttpStatusCode.Accepted,a.StatusCode); Assert.Equal(HttpStatusCode.Accepted,b.StatusCode);
        Assert.Equal(await a.Content.ReadAsStringAsync(),await b.Content.ReadAsStringAsync());
        Assert.Empty(f.DesafiosCorreo);
    }

    [Fact]
    public async Task Vencimiento_GetYPost_NoHayConsumoAutomatico()
    {
        using var f=new JwtApiFactory(); var clock=new Clock();
        using var host=Host(f,clock:clock); using var c=Cliente(host);
        var u=await Registrar(f,c);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await c.GetAsync("/api/auth/email/verificar")).StatusCode);
        Assert.Null(f.DesafiosCorreo[u.DesafioId].ConsumidoEnUtc);
        clock.Now=clock.Now.AddHours(24);
        var r=await c.PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=u.DesafioId,token=u.Token,password=Password});
        Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
        Assert.Null(f.DesafiosCorreo[u.DesafioId].ConsumidoEnUtc);
    }

    [Fact]
    public async Task Ip_ComparteDiezSolicitudes_NoConfiaEnForwardedFor()
    {
        using var f=new JwtApiFactory();
        using var host=Host(f,new(){["VerificacionCorreo:PublicRegistrationEnabled"]="false"});
        using var c=Cliente(host);
        for(var i=0;i<10;i++)
        {
            c.DefaultRequestHeaders.Remove("X-Forwarded-For");
            c.DefaultRequestHeaders.Add("X-Forwarded-For",$"198.51.100.{i+1}");
            var r=await c.PostAsJsonAsync("/api/usuarios",new{nombre="Ana",email="nuevo@example.invalid",password=Password,timeZoneId="Etc/UTC"});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,r.StatusCode);
        }
        var limit=await c.PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=Guid.NewGuid(),token=new string('A',43),password=Password});
        Assert.Equal(HttpStatusCode.TooManyRequests,limit.StatusCode);
        Assert.Empty(f.DesafiosCorreo);
    }

    [Fact]
    public async Task PresupuestoGlobal_NoCreaCuentaCuandoNoPuedeReservar()
    {
        using var f=new JwtApiFactory(); using var host=Host(f,new(){["VerificacionCorreo:Limites:EmisionesGlobales"]="1"});
        using var c=Cliente(host); await Registrar(f,c);
        var email="segunda@example.invalid";
        var r=await c.PostAsJsonAsync("/api/usuarios",new{nombre="Ana",email,password=Password,timeZoneId="Etc/UTC"});
        Assert.Equal(HttpStatusCode.ServiceUnavailable,r.StatusCode);
        Assert.DoesNotContain(f.UsuariosPersistidos.Values,x=>x.Email==email);
        Assert.Single(f.DesafiosCorreo);
    }

    [Fact]
    public async Task FalloEntrega_ConservaCuentaPendienteYEmision()
    {
        using var f=new JwtApiFactory{FallarEntregaCorreo=true};
        using var host=Host(f); using var c=Cliente(host);
        var u=await Registrar(f,c);
        Assert.Equal(EstadoVerificacionCorreo.Pendiente,f.UsuariosPersistidos[u.UserId].EstadoVerificacionCorreo);
        Assert.Single(f.DesafiosCorreo);
    }

    [Fact]
    public async Task CierreDeAltas_NoBloqueaConfirmacionExistente()
    {
        using var f=new JwtApiFactory(); using var abierto=Host(f);
        var u=await Registrar(f,Cliente(abierto));
        using var cerrado=Host(f,new(){["VerificacionCorreo:PublicRegistrationEnabled"]="false"});
        var r=await Cliente(cerrado).PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=u.DesafioId,token=u.Token,password=Password});
        Assert.Equal(HttpStatusCode.NoContent,r.StatusCode);
    }


    [Fact]
    public async Task Ip_VentanaMovilExacta_UsaRelojYRecuperaAlCumplir10Minutos()
    {
        using var f=new JwtApiFactory(); var clock=new Clock();
        using var host=Host(f,new(){["VerificacionCorreo:PublicRegistrationEnabled"]="false"},clock:clock);
        using var c=Cliente(host);
        async Task<HttpStatusCode> Pedir() => (await c.PostAsJsonAsync("/api/auth/email/reenviar",new{email="a@example.invalid"})).StatusCode;
        for(var i=0;i<10;i++) Assert.Equal(HttpStatusCode.ServiceUnavailable,await Pedir());
        clock.Now=clock.Now.AddMinutes(10).AddTicks(-1);
        Assert.Equal(HttpStatusCode.TooManyRequests,await Pedir());
        clock.Now=clock.Now.AddTicks(1);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,await Pedir());
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now=DateTimeOffset.Parse("2026-10-21T00:00:00Z");
        public override DateTimeOffset GetUtcNow()=>Now;
    }
}
