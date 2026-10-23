using System.Text.RegularExpressions;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Security;
using Constia.Infrastructure.Usuarios;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Constia.API.Tests;

// Exclusivamente opt-in en el servicio SQL Server 2022 desechable cuya identidad se verifica antes de CREATE DATABASE.
public sealed class EmailVerificationSqlServerTests
{
    private const string EnabledVariable="CONSTIA_AUTHEMAIL01B_SQLSERVER_TESTS";
    private const string ConnectionVariable="CONSTIA_AUTHEMAIL01B_SQLSERVER_CONNECTION";
    private const string ContainerVariable="CONSTIA_AUTHEMAIL01B_CONTAINER_ID";
    private const string Previous="20261020071619_AddHabitIconId";
    private const string Current="20261021062129_AddEmailVerification";
    private const string Password="ABCDE1.@";
    private static readonly AspNetCorePasswordHasher Hasher=new();
    private static readonly PoliticaVerificacionCorreo Policy=new();

    private static VerificacionCorreoRepository Repo(ConstiaDbContext db,Clock clock)=>new(db,Hasher,clock);
    private static Usuario Nuevo()=>new("Ana",$"{Guid.NewGuid()}@example.invalid",Hasher.HashPassword(Password),"Etc/UTC");
    private static TokenVerificacion Token()=>new GeneradorTokenVerificacion().Generar();
    private static async Task Init(EmailVerificationSqlDatabase d)
    {
        await using var db=d.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(Current);
    }
    private static async Task<(Usuario U,TokenVerificacion T,Guid Id)> Alta(EmailVerificationSqlDatabase d,Clock clock)
    {
        await using var db=d.CreateContext();
        var u=Nuevo(); var token=Token();
        var r=await Repo(db,clock).RegistrarAsync(u,token.Hash,Policy,default);
        Assert.Equal(EstadoReservaCorreo.Creado,r.Estado);
        return(u,token,r.Emision!.DesafioId);
    }

    [EmailSqlFact]
    public async Task Up_PreservaLegadoHashZonaYLoginSinFechaInventada()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync();
        var id=Guid.NewGuid(); var hash=Hasher.HashPassword("vieja");
        await using(var db=d.CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(Previous);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [Usuario] ([Id],[Nombre],[Email],[PasswordHash],[TimeZoneId]) VALUES ({id},{"Legado"},{"legado@example.invalid"},{hash},{"Etc/UTC"})");
            await db.GetService<IMigrator>().MigrateAsync(Current);
        }
        await using var verify=d.CreateContext();
        var u=await verify.Set<Usuario>().SingleAsync();
        Assert.Equal(EstadoVerificacionCorreo.LegadoExento,u.EstadoVerificacionCorreo);
        Assert.Null(u.EmailVerificadoEnUtc); Assert.Equal(hash,u.PasswordHash); Assert.Equal("Etc/UTC",u.TimeZoneId); Assert.Equal(id,u.Id);
        var jwt=new JwtObservado();
        var login=await new AutenticarUsuario(new UsuarioRepository(verify),Hasher,jwt).EjecutarAsync(u.Email,"vieja");
        Assert.Equal(EstadoAutenticacion.Autenticado,login.Estado); Assert.Equal(1,jwt.Count);
    }

    [EmailSqlFact]
    public async Task DefaultsYTrigger_AltasPendientesYRechazoDeLegadoNuevo()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        await using var db=d.CreateContext();
        var id=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [Usuario] ([Id],[Nombre],[Email],[PasswordHash],[TimeZoneId]) VALUES ({id},{"Ana"},{"a@example.invalid"},{"hash"},{"Etc/UTC"})");
        Assert.Equal(EstadoVerificacionCorreo.Pendiente,(await db.Set<Usuario>().SingleAsync()).EstadoVerificacionCorreo);
        var bad=Guid.NewGuid();
        var ex=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [Usuario] ([Id],[Nombre],[Email],[PasswordHash],[TimeZoneId],[EstadoVerificacionCorreo]) VALUES ({bad},{"Ana"},{"b@example.invalid"},{"hash"},{"Etc/UTC"},{0})"));
        Assert.Equal(51031,ex.Number);
        Assert.Equal(1,await db.Set<Usuario>().CountAsync());
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Usuario] SET [EstadoVerificacionCorreo]=2 WHERE [Id]={id}"));
        Assert.Null((await db.Set<Usuario>().AsNoTracking().SingleAsync()).EmailVerificadoEnUtc);
    }

    [EmailSqlFact]
    public async Task Constraints_HashPropositoUnicidadYFk()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock=new Clock(); var a=await Alta(d,clock);
        await using var db=d.CreateContext();
        var id=Guid.NewGuid(); var token=Token();
        db.Add(new DesafioVerificacionCorreo(a.U.Id,a.U.Email,token.Hash,clock.Now));
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [DesafioVerificacionCorreo] SET [Proposito]=2 WHERE [Id]={a.Id}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [DesafioVerificacionCorreo] SET [TokenHash]={new byte[31]} WHERE [Id]={a.Id}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Usuario] WHERE [Id]={a.U.Id}"));
        Assert.Equal(1,await db.Set<DesafioVerificacionCorreo>().CountAsync());
    }

    [EmailSqlFact]
    public async Task PasswordIncorrecta_NoConsumeLuegoConfirmaYReintentoNoDuplica()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock=new Clock(); var a=await Alta(d,clock);
        await using(var db=d.CreateContext())
            Assert.Equal(ResultadoConfirmacionCorreo.Invalido,await Repo(db,clock).ConfirmarAsync(a.Id,a.T.Hash,"incorrecta",Policy,default));
        await using(var db=d.CreateContext())
        {
            var previo=await db.Set<DesafioVerificacionCorreo>().AsNoTracking().SingleAsync();
            Assert.Null(previo.ConsumidoEnUtc); Assert.Equal(1,previo.IntentosConfirmacion);
            Assert.Equal(ResultadoConfirmacionCorreo.Confirmado,await Repo(db,clock).ConfirmarAsync(a.Id,a.T.Hash,Password,Policy,default));
        }
        await using(var db=d.CreateContext())
        {
            Assert.Equal(ResultadoConfirmacionCorreo.Usado,await Repo(db,clock).ConfirmarAsync(a.Id,a.T.Hash,Password,Policy,default));
            var u=await db.Set<Usuario>().AsNoTracking().SingleAsync();
            var desafio=await db.Set<DesafioVerificacionCorreo>().AsNoTracking().SingleAsync();
            Assert.Equal(EstadoVerificacionCorreo.Verificado,u.EstadoVerificacionCorreo);
            Assert.Equal(clock.Now,u.EmailVerificadoEnUtc); Assert.Equal(clock.Now,desafio.ConsumidoEnUtc);
            Assert.Equal(a.U.PasswordHash,u.PasswordHash);
        }
    }

    [EmailSqlFact]
    public async Task DosConfirmaciones_UnGanador()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock=new Clock(); var a=await Alta(d,clock);
        async Task<ResultadoConfirmacionCorreo> Confirmar()
        {
            await using var db=d.CreateContext();
            return await Repo(db,clock).ConfirmarAsync(a.Id,a.T.Hash,Password,Policy,default);
        }
        var results=await Task.WhenAll(Confirmar(),Confirmar());
        Assert.Single(results,x=>x==ResultadoConfirmacionCorreo.Confirmado);
        Assert.Single(results,x=>x==ResultadoConfirmacionCorreo.Usado);
    }

    [EmailSqlFact]
    public async Task DosReenvios_UnGanadorYTokenAnteriorSustituido()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock=new Clock(); var a=await Alta(d,clock); clock.Now=clock.Now.AddSeconds(60);
        async Task<EmisionCorreo?> Enviar()
        {
            await using var db=d.CreateContext();
            return await Repo(db,clock).ReenviarAsync(a.U.Email,Token().Hash,Policy,default);
        }
        var results=await Task.WhenAll(Enviar(),Enviar());
        Assert.Single(results,x=>x is not null);
        await using var verify=d.CreateContext();
        Assert.Equal(2,await verify.Set<DesafioVerificacionCorreo>().CountAsync());
        Assert.Equal(ResultadoConfirmacionCorreo.Reemplazado,await Repo(verify,clock).ConfirmarAsync(a.Id,a.T.Hash,Password,Policy,default));
    }

    [EmailSqlFact]
    public async Task ConfirmacionContraReenvio_NoHayEstadoParcial()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock=new Clock(); var a=await Alta(d,clock); clock.Now=clock.Now.AddSeconds(60);
        await using var c1=d.CreateContext(); await using var c2=d.CreateContext();
        var confirm=Repo(c1,clock).ConfirmarAsync(a.Id,a.T.Hash,Password,Policy,default);
        var reenviar=Repo(c2,clock).ReenviarAsync(a.U.Email,Token().Hash,Policy,default);
        await Task.WhenAll(confirm,reenviar);
        await using var verify=d.CreateContext();
        var u=await verify.Set<Usuario>().AsNoTracking().SingleAsync();
        var original=await verify.Set<DesafioVerificacionCorreo>().AsNoTracking().SingleAsync(x=>x.Id==a.Id);
        if(confirm.Result==ResultadoConfirmacionCorreo.Confirmado)
        { Assert.Null(reenviar.Result); Assert.NotNull(original.ConsumidoEnUtc); Assert.Equal(EstadoVerificacionCorreo.Verificado,u.EstadoVerificacionCorreo); }
        else
        { Assert.NotNull(reenviar.Result); Assert.Equal(ResultadoConfirmacionCorreo.Reemplazado,confirm.Result); Assert.Null(original.ConsumidoEnUtc); Assert.NotNull(original.InvalidadoEnUtc); Assert.Equal(EstadoVerificacionCorreo.Pendiente,u.EstadoVerificacionCorreo); }
    }

    [EmailSqlFact]
    public async Task PresupuestoGlobalConcurrente_NoSupera40YExpiraExactamenteEn24Horas()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync(); await Init(d); var clock=new Clock();
        for(var i=0;i<39;i++) await Alta(d,clock);
        async Task<ReservaRegistroCorreo> AltaConcurrente()
        {
            await using var db=d.CreateContext();
            return await Repo(db,clock).RegistrarAsync(Nuevo(),Token().Hash,Policy,default);
        }
        var results=await Task.WhenAll(AltaConcurrente(),AltaConcurrente());
        Assert.Single(results,x=>x.Estado==EstadoReservaCorreo.Creado);
        Assert.Single(results,x=>x.Estado==EstadoReservaCorreo.PresupuestoAgotado);
        await using(var verify=d.CreateContext())
        {
            Assert.Equal(40,await verify.Set<Usuario>().CountAsync());
            Assert.Equal(40,await verify.Set<DesafioVerificacionCorreo>().CountAsync());
            Assert.Equal(40,await verify.Set<PresupuestoEmisionCorreo>().Select(x=>x.TotalEmisiones).SingleAsync());
        }
        clock.Now=clock.Now.AddHours(24);
        Assert.Equal(EstadoReservaCorreo.Creado,(await AltaConcurrente()).Estado);
    }

    [EmailSqlFact]
    public async Task CincoEmisiones_CuentanReemplazosYRecuperanVentana()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync(); await Init(d); var clock=new Clock();
        var a=await Alta(d,clock);
        for(var i=0;i<4;i++)
        {
            clock.Now=clock.Now.AddSeconds(60);
            await using var db=d.CreateContext();
            Assert.NotNull(await Repo(db,clock).ReenviarAsync(a.U.Email,Token().Hash,Policy,default));
        }
        clock.Now=clock.Now.AddSeconds(60);
        await using(var db=d.CreateContext())
            Assert.Null(await Repo(db,clock).ReenviarAsync(a.U.Email,Token().Hash,Policy,default));
        clock.Now=clock.Now.AddHours(24);
        await using(var db=d.CreateContext())
        {
            Assert.NotNull(await Repo(db,clock).ReenviarAsync(a.U.Email,Token().Hash,Policy,default));
            Assert.Equal(6,await db.Set<DesafioVerificacionCorreo>().CountAsync());
        }
    }

    [EmailSqlFact]
    public async Task VencimientoYEmailVinculado_RechazanSinConsumir()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync(); await Init(d); var clock=new Clock(); var a=await Alta(d,clock);
        await using(var db=d.CreateContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [DesafioVerificacionCorreo] SET [EmailDestino]={"otro@example.invalid"} WHERE [Id]={a.Id}");
            Assert.Equal(ResultadoConfirmacionCorreo.Invalido,await Repo(db,clock).ConfirmarAsync(a.Id,a.T.Hash,Password,Policy,default));
        }
        clock.Now=clock.Now.AddHours(24);
        await using(var db=d.CreateContext())
        {
            Assert.Equal(ResultadoConfirmacionCorreo.Vencido,await Repo(db,clock).ConfirmarAsync(a.Id,a.T.Hash,Password,Policy,default));
            Assert.Null((await db.Set<DesafioVerificacionCorreo>().AsNoTracking().SingleAsync()).ConsumidoEnUtc);
        }
    }

    [EmailSqlFact]
    public async Task FalloDespuesDeSql_AltaRevierteUsuarioDesafioYPresupuesto()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync(); await Init(d); var clock=new Clock();
        await using(var db=d.CreateContext(new FalloDespuesGuardar()))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>Repo(db,clock).RegistrarAsync(Nuevo(),Token().Hash,Policy,default));
        await using var verify=d.CreateContext();
        Assert.Empty(await verify.Set<Usuario>().ToArrayAsync());
        Assert.Empty(await verify.Set<DesafioVerificacionCorreo>().ToArrayAsync());
        Assert.Equal(0,await verify.Set<PresupuestoEmisionCorreo>().Select(x=>x.TotalEmisiones).SingleAsync());
    }

    [EmailSqlFact]
    public async Task FalloDespuesDeSql_ConfirmacionRevierteEstadoYConsumo()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync(); await Init(d); var clock=new Clock(); var a=await Alta(d,clock);
        await using(var db=d.CreateContext(new FalloDespuesGuardar()))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>Repo(db,clock).ConfirmarAsync(a.Id,a.T.Hash,Password,Policy,default));
        await using var verify=d.CreateContext();
        Assert.Equal(EstadoVerificacionCorreo.Pendiente,(await verify.Set<Usuario>().SingleAsync()).EstadoVerificacionCorreo);
        var desafio=await verify.Set<DesafioVerificacionCorreo>().SingleAsync();
        Assert.Null(desafio.ConsumidoEnUtc); Assert.Equal(0,desafio.IntentosConfirmacion);
    }

    [EmailSqlFact]
    public async Task Down_LegadoSolamentePermiteReversion()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync();
        await using var db=d.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(Previous);
        var id=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [Usuario] ([Id],[Nombre],[Email],[PasswordHash],[TimeZoneId]) VALUES ({id},{"Legado"},{"legado@example.invalid"},{"hash"},{"Etc/UTC"})");
        await db.GetService<IMigrator>().MigrateAsync(Current);
        await db.GetService<IMigrator>().MigrateAsync(Previous);
        Assert.Equal(Previous,(await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(1,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [Usuario]").SingleAsync());
    }

    [EmailSqlFact]
    public async Task Down_ConCuentaNuevaRechaza51030SinPerdidaParcial()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync(); await Init(d); var clock=new Clock(); var a=await Alta(d,clock);
        await using var db=d.CreateContext();
        var history=(await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var ex=await Assert.ThrowsAsync<SqlException>(()=>db.GetService<IMigrator>().MigrateAsync(Previous));
        Assert.Equal(51030,ex.Number);
        Assert.Equal(history,(await db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(1,await db.Set<Usuario>().CountAsync());
        Assert.Equal(1,await db.Set<DesafioVerificacionCorreo>().CountAsync());
        Assert.True(await Repo(db,clock).EsquemaDisponibleAsync(default));
        Assert.True(HashTokenVerificacion.Coincide(a.T.Hash,(await db.Set<DesafioVerificacionCorreo>().SingleAsync()).TokenHash));
    }


    [EmailSqlFact]
    public async Task ContextoConTrackingObsoleto_NoReenviaDespuesDeConfirmacionExterna()
    {
        await using var d=await EmailVerificationSqlDatabase.CreateAsync(); await Init(d); var clock=new Clock(); var a=await Alta(d,clock);
        await using var obsoleto=d.CreateContext();
        var previo=await obsoleto.Set<Usuario>().SingleAsync();
        Assert.Equal(EstadoVerificacionCorreo.Pendiente,previo.EstadoVerificacionCorreo);
        await obsoleto.Set<PresupuestoEmisionCorreo>().SingleAsync();
        clock.Now=clock.Now.AddSeconds(60);
        await using(var externo=d.CreateContext())
            Assert.Equal(ResultadoConfirmacionCorreo.Confirmado,await Repo(externo,clock).ConfirmarAsync(a.Id,a.T.Hash,Password,Policy,default));
        Assert.Null(await Repo(obsoleto,clock).ReenviarAsync(a.U.Email,Token().Hash,Policy,default));
        await using var verify=d.CreateContext();
        Assert.Equal(1,await verify.Set<DesafioVerificacionCorreo>().CountAsync());
        Assert.Equal(1,await verify.Set<PresupuestoEmisionCorreo>().Select(x=>x.TotalEmisiones).SingleAsync());
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now=DateTimeOffset.Parse("2026-10-21T00:00:00Z");
        public override DateTimeOffset GetUtcNow()=>Now;
    }
    private sealed class JwtObservado : IAccessTokenService
    {
        public int Count;
        public AccessToken Emitir(Guid id,string n,string e) { Count++; return new("ficticio",DateTimeOffset.MaxValue); }
    }
    private sealed class FalloDespuesGuardar : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,int result,CancellationToken ct=default) =>
            throw new InvalidOperationException("Fallo simulado después de SQL y antes del commit.");
    }

    private static bool IsEnabled() =>
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
        && Environment.GetEnvironmentVariable(EnabledVariable) == "true"
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable));

    private sealed class EmailSqlFactAttribute : FactAttribute
    {
        public EmailSqlFactAttribute()
        {
            if (!IsEnabled()) Skip = "Requires the explicitly enabled disposable AUTH-EMAIL-01-B SQL Server CI service.";
        }
    }

}
