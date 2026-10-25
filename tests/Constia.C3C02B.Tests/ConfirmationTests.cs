using System.Net;
using System.Net.Http.Json;
using Constia.Application.Bienvenida;
using Constia.Application.Usuarios;
using Constia.C3C02B.Harness;
using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Security;
using Constia.Infrastructure.Usuarios;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Constia.C3C02B.Tests;

// Only TestServer in-memory HTTP. No listening socket, real provider, or product endpoints are added.
internal sealed class ConfirmationFactory : WebApplicationFactory<global::Program>
{
    private readonly DisposableSqlDatabase database;
    private readonly Dictionary<string,string?> previous=[];
    private readonly bool aligned;
    public ConfirmationFactory(DisposableSqlDatabase db,bool aligned)
    {
        database=db;this.aligned=aligned;
        var values=new Dictionary<string,string?> {
            ["ConnectionStrings__DefaultConnection"]=db.Identity.Connection("runtime",db.Identity.Database).ConnectionString,
            ["JwtSettings__SigningKey"]="c3c02b-synthetic-signing-key-not-a-real-secret-64-bytes",
            ["JwtSettings__Issuer"]="synthetic",["JwtSettings__Audience"]="synthetic",["JwtSettings__ExpirationMinutes"]="10",
            ["Bienvenida__CapturarEventos"]="true",["Bienvenida__EpocaCaptura"]=(aligned?DisposableSqlDatabase.Epoch:Guid.NewGuid()).ToString(),
            ["Bienvenida__ProcesamientoHabilitado"]="false",["Bienvenida__EnvioHabilitado"]="false",
            ["Bienvenida__MantenimientoHabilitado"]="false",["VerificacionCorreo__C2Enabled"]="false",
            ["VerificacionCorreo__PublicRegistrationEnabled"]="false",["Resend__Enabled"]="false",
            ["ASPNETCORE_TEST_CONTENTROOT_CONSTIA_API"]=AppContext.BaseDirectory
        };
        foreach(var(k,v)in values) {previous[k]=Environment.GetEnvironmentVariable(k);Environment.SetEnvironmentVariable(k,v);}
    }
    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        b.UseEnvironment("Testing");b.UseContentRoot(AppContext.BaseDirectory);
        b.ConfigureLogging(x=>x.ClearProviders());
        b.ConfigureTestServices(s=>{
            s.RemoveAll<DbContextOptions<ConstiaDbContext>>();
            s.AddDbContext<ConstiaDbContext>(o=>o.UseSqlServer(database.Identity.Connection("runtime",database.Identity.Database).ConnectionString)
                .AddInterceptors(new IdentityConnectionInterceptor(database.Identity)));
            s.RemoveAll<OpcionesCapturaBienvenida>();
            s.AddSingleton(OpcionesCapturaBienvenida.DesdeValores("true",
                (aligned?DisposableSqlDatabase.Epoch:Guid.NewGuid()).ToString()));
            foreach(var name in new[]{"CONSTIA.Resend.C1","CONSTIA.Resend.C3"})
                s.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(()=>new SimulatedResendHandler(new()));
        });
    }
    public override async ValueTask DisposeAsync()
    {
        try {await base.DisposeAsync();}
        finally {foreach(var(k,v)in previous)Environment.SetEnvironmentVariable(k,v);}
    }
}

[Collection("Sandbox")]
public sealed class ConfirmationTests
{
    [Fact,Trait("Category","Operational")]
    public async Task CaptureMismatchHttpIsAtomicAndCoordinatedReopenPreservesLogin()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();
        const string password="ABCDE1.@";
        var hasher=new AspNetCorePasswordHasher();var token=new GeneradorTokenVerificacion().Generar();
        var user=new Usuario("Synthetic",Guid.NewGuid().ToString("N")+"@example.invalid",hasher.HashPassword(password),"Etc/UTC");
        Guid challenge;
        await using(var d=f.Db("migrator")) {
            var repo=new VerificacionCorreoRepository(d,hasher,TimeProvider.System);
            var created=await repo.RegistrarAsync(user,token.Hash,new(),default);
            Assert.Equal(EstadoReservaCorreo.Creado,created.Estado);challenge=created.Emision!.DesafioId;
        }
        await using(var closed=new ConfirmationFactory(f,false)) {
            using var client=closed.CreateClient(new(){BaseAddress=new Uri("https://localhost")});
            using var denied=await client.PostAsJsonAsync("/api/auth/email/verificar",new{DesafioId=challenge,Token=token.Valor,Password=password});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,denied.StatusCode);
            await using var d=f.Db("migrator");
            Assert.Equal(EstadoVerificacionCorreo.Pendiente,(await d.Set<Usuario>().AsNoTracking().SingleAsync()).EstadoVerificacionCorreo);
            Assert.Null((await d.Set<DesafioVerificacionCorreo>().AsNoTracking().SingleAsync()).ConsumidoEnUtc);
            Assert.Empty(await d.Set<EventoBienvenida>().ToArrayAsync());
            // Disposal drains the TestServer before the aligned host is created.
        }
        await using(var opened=new ConfirmationFactory(f,true)) {
            using var client=opened.CreateClient(new(){BaseAddress=new Uri("https://localhost")});
            using var confirmed=await client.PostAsJsonAsync("/api/auth/email/verificar",new{DesafioId=challenge,Token=token.Valor,Password=password});
            Assert.Equal(HttpStatusCode.NoContent,confirmed.StatusCode);
            using var login=await client.PostAsJsonAsync("/api/auth/login",new{Email=user.Email,Password=password});
            Assert.Equal(HttpStatusCode.OK,login.StatusCode); // JWT remains in memory and is never logged/exported.
        }
        await using(var d=f.Db("migrator")) {
            Assert.Single(await d.Set<EventoBienvenida>().ToArrayAsync());
            Assert.Equal(EstadoVerificacionCorreo.Verificado,(await d.Set<Usuario>().SingleAsync()).EstadoVerificacionCorreo);
            Assert.NotNull((await d.Set<DesafioVerificacionCorreo>().SingleAsync()).ConsumidoEnUtc);
            Assert.Empty(await d.Set<Constia.Infrastructure.Bienvenida.IntentoBienvenida>().ToArrayAsync());
        }
    }
}
