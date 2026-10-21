using System.Net;
using System.Net.Http.Json;
using Constia.API.Authentication;
using Constia.API.Configuration;
using Constia.Application.Correo;
using Constia.Application.Usuarios;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
namespace Constia.API.Tests;

public sealed class ResendC1HostTests
{
    private sealed class Host(bool enabled):WebApplicationFactory<Program>
    {
        public ResendHttpClientTests.Handler Http {get;}=new((_,_)=>throw new InvalidOperationException("Red no autorizada"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
                ["ConnectionStrings:DefaultConnection"]="Server=unused;Database=unused;Trusted_Connection=True",
                ["JwtSettings:SigningKey"]="synthetic-test-signing-key-only-32-bytes",
                ["JwtSettings:Issuer"]="tests",["JwtSettings:Audience"]="tests",["JwtSettings:ExpirationMinutes"]="10",
                ["VerificacionCorreo:PublicRegistrationEnabled"]="true",["VerificacionCorreo:PublicUrl"]="https://constia.example",
                ["Resend:Enabled"]=enabled.ToString(),["Resend:ApiKey"]=enabled?"synthetic-key-not-a-secret":null,
                ["Resend:FromAddress"]=enabled?"correo@example.invalid":null
            }));
            builder.ConfigureTestServices(s=>{
                s.Configure<HttpClientFactoryOptions>(ResendC1ServiceCollectionExtensions.NombreCliente,o=>
                    o.HttpMessageHandlerBuilderActions.Add(b=>b.PrimaryHandler=Http));
            });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostArranca_NoActivaB_NiHaceEnvios(bool enabled)
    {
        using var host=new Host(enabled);using var client=host.CreateClient();
        using var scope=host.Services.CreateScope();
        var b=scope.ServiceProvider.GetRequiredService<IEntregaCorreoVerificacion>();
        Assert.IsType<EntregaCorreoNoDisponible>(b);
        var c1=scope.ServiceProvider.GetRequiredService<ITransporteCorreoVerificacion>();
        Assert.False(c1 is IEntregaCorreoProduccion);Assert.False(c1 is IEntregaCorreoDePrueba);
        Assert.Equal(0,host.Http.Calls);
        var response=await client.PostAsJsonAsync("/api/usuarios",new{
            nombre="Ana",email="nuevo@example.invalid",password="ABCDE1.@",timeZoneId="Etc/UTC"});
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
        var resend=await client.PostAsJsonAsync("/api/auth/email/reenviar",new{email="nuevo@example.invalid"});
        Assert.Equal(HttpStatusCode.ServiceUnavailable,resend.StatusCode);
        var confirmation=await client.PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=Guid.Empty,token="invalid",password="password"});
        Assert.Equal(HttpStatusCode.BadRequest,confirmation.StatusCode);
        Assert.Equal(0,host.Http.Calls);
    }

    [Fact]
    public async Task FakeNoHabilitaProduccion_YLoginYConfirmacionDeBSeConservan()
    {
        using var f=new JwtApiFactory();
        using var production=f.WithWebHostBuilder(b=>b.UseEnvironment("Production"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await production.CreateClient().PostAsJsonAsync(
            "/api/usuarios",new{nombre="Ana",email="nuevo@example.invalid",password="ABCDE1.@",timeZoneId="Etc/UTC"})).StatusCode);
        using var verified=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{
            s.RemoveAll<IUsuarioPasswordHasher>();
            s.AddScoped<IUsuarioPasswordHasher,Constia.Infrastructure.Security.AspNetCorePasswordHasher>();
        }));
        using var client=verified.CreateClient();
        var register=await client.PostAsJsonAsync("/api/usuarios",new{nombre="Ana",email="ana-c1@example.invalid",password="ABCDE1.@",timeZoneId="Etc/UTC"});
        Assert.Equal(HttpStatusCode.Created,register.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/auth/login",new{email="ana-c1@example.invalid",password="ABCDE1.@"})).StatusCode);
        var mail=Assert.Single(f.CorreosDePrueba);
        Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/auth/email/verificar",new{desafioId=mail.Id,token=mail.Token,password="ABCDE1.@"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/auth/login",new{email="ana-c1@example.invalid",password="ABCDE1.@"})).StatusCode);
    }
}
