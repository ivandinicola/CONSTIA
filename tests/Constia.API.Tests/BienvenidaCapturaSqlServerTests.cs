using System.Data.Common;
using Constia.Application.Bienvenida;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Bienvenida;
using Constia.Infrastructure.Security;
using Constia.Infrastructure.Usuarios;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Constia.API.Tests;

// Cada caso exige el servicio SQL descartable verificado por EmailVerificationSqlDatabase.
// No hay HTTP, worker ni reservas productivas: los estados futuros se preparan solo mediante SQL de prueba.
public sealed class BienvenidaCapturaSqlServerTests
{
    private const string Previous = "20261021062129_AddEmailVerification";
    private const string Password = "ABCDE1.@";
    private static readonly Guid Epoch = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly AspNetCorePasswordHasher Hasher = new();
    private static readonly PoliticaVerificacionCorreo Policy = new();
    private static OpcionesCapturaBienvenida Enabled => OpcionesCapturaBienvenida.DesdeValores("true", Epoch.ToString());
    private static VerificacionCorreoRepository Repo(ConstiaDbContext db, Clock clock, OpcionesCapturaBienvenida? options = null) =>
        new(db, Hasher, clock, options ?? Enabled);

    private static async Task Init(EmailVerificationSqlDatabase d, bool active = true)
    {
        await using var db = d.CreateContext();
        await db.Database.MigrateAsync();
        if (active)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.ControlBienvenida SET CapturaHabilitada=1,EpocaCaptura={Epoch},
                InicioCapturaEnUtc=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00') WHERE Id=1
                """);
    }
    private static async Task<(Usuario U, TokenVerificacion T, Guid Id)> Alta(EmailVerificationSqlDatabase d, Clock clock)
    {
        await using var db = d.CreateContext();
        var u = new Usuario("Ana", $"{Guid.NewGuid()}@example.invalid", Hasher.HashPassword(Password), "Etc/UTC");
        var token = new GeneradorTokenVerificacion().Generar();
        var r = await Repo(db, clock).RegistrarAsync(u, token.Hash, Policy, default);
        Assert.Equal(EstadoReservaCorreo.Creado, r.Estado);
        return (u, token, r.Emision!.DesafioId);
    }
    private static async Task Confirmar(EmailVerificationSqlDatabase d, Clock clock, (Usuario U, TokenVerificacion T, Guid Id) a)
    {
        await using var db = d.CreateContext();
        Assert.Equal(ResultadoConfirmacionCorreo.Confirmado, await Repo(db, clock).ConfirmarAsync(a.Id, a.T.Hash, Password, Policy, default));
    }
    private static async Task SinTransicion(EmailVerificationSqlDatabase d)
    {
        await using var db = d.CreateContext();
        Assert.Equal(EstadoVerificacionCorreo.Pendiente, (await db.Set<Usuario>().SingleAsync()).EstadoVerificacionCorreo);
        Assert.Null((await db.Set<DesafioVerificacionCorreo>().SingleAsync()).ConsumidoEnUtc);
        Assert.Equal(0, await db.Set<EventoBienvenida>().CountAsync());
    }
    private static async Task<long> Cuota(EmailVerificationSqlDatabase d)
    {
        await using var db = d.CreateContext();
        return await db.Set<PresupuestoEmisionCorreo>().Select(x => x.TotalEmisiones).SingleAsync();
    }

    [C3ASqlFact]
    public async Task Up_AgregaSoloOutboxVaciaYControlDesactivadoSinBackfill()
    {
        await using var d = await EmailVerificationSqlDatabase.CreateAsync();
        await using var db = d.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(Previous);
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.Usuario (Id,Nombre,Email,PasswordHash,TimeZoneId) VALUES ({id},{"Anterior"},{"anterior@example.invalid"},{"hash"},{"Etc/UTC"})");
        var tables = await db.Database.SqlQueryRaw<string>("SELECT name AS [Value] FROM sys.tables WHERE is_ms_shipped=0").ToArrayAsync();
        await db.Database.MigrateAsync();
        var after = await db.Database.SqlQueryRaw<string>("SELECT name AS [Value] FROM sys.tables WHERE is_ms_shipped=0").ToArrayAsync();
        Assert.Equal(new[] { "ControlBienvenida", "EventoBienvenida", "IntentoBienvenida" }, after.Except(tables).Order().ToArray());
        Assert.Equal(id, (await db.Set<Usuario>().SingleAsync()).Id);
        Assert.Empty(await db.Set<EventoBienvenida>().ToArrayAsync());
        Assert.Empty(await db.Set<IntentoBienvenida>().ToArrayAsync());
        var c = await db.Set<ControlBienvenida>().SingleAsync();
        Assert.False(c.CapturaHabilitada); Assert.False(c.EnvioHabilitado); Assert.False(c.PausaProveedor);
        Assert.Null(c.EpocaCaptura); Assert.Equal(1, c.VersionEsquema); Assert.Equal(1, c.ProtocoloEscritor);
    }

    [C3ASqlFact]
    public async Task Restricciones_ImponenUnicidadFkEstadosYCoherenciaDeCampos()
    {
        await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock = new Clock(); var a = await Alta(d, clock); await Confirmar(d, clock, a);
        await using var db = d.CreateContext();
        var e = await db.Set<EventoBienvenida>().SingleAsync();
        db.Add(EventoBienvenida.CrearPendiente(a.U.Id, Epoch, a.U.Email, clock.Now, clock.Now));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(((SqlException)duplicate.InnerException!).Number, new[] { 2601, 2627 }); db.ChangeTracker.Clear();
        var missing = EventoBienvenida.CrearPendiente(Guid.NewGuid(), Epoch, "missing@example.invalid", clock.Now, clock.Now);
        db.Add(missing);
        var fk = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(547, ((SqlException)fk.InnerException!).Number); db.ChangeTracker.Clear();
        foreach (var sql in new[] {
            "UPDATE dbo.EventoBienvenida SET Reservas=4",
            "UPDATE dbo.EventoBienvenida SET Estado=3",
            "UPDATE dbo.EventoBienvenida SET PayloadUtf8=0x01",
            "UPDATE dbo.EventoBienvenida SET ConfirmadoEnUtc=SWITCHOFFSET(ConfirmadoEnUtc,'+01:00')",
            "UPDATE dbo.ControlBienvenida SET CapturaHabilitada=0",
            "DELETE FROM dbo.Usuario" })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql)); Assert.Equal(547, ex.Number);
        }
        // Un estado futuro válido y un intento reservado son admitidos; no los crea C3-A.
        var lease = Guid.NewGuid(); var now = clock.Now;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET Estado=3,Reservas=1,PrimeraReservaEnUtc=CapturadoEnUtc,
            HorizonteHttpEnUtc=DATEADD(hour,23,CapturadoEnUtc),LeaseId={lease},LeaseGeneracion=1,
            LeaseHastaUtc={now},UltimoLeaseHastaUtc={now},PayloadUtf8=0x01,PayloadSha256={new byte[32]},PerfilProveedorVersion='test-v1'
            WHERE Id={e.Id}
            """);
        var attempt = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.IntentoBienvenida (Id,EventoId,Ordinal,ReservadoEnUtc,PropietarioLeaseId,LeaseGeneracion,Estado)
            VALUES ({attempt},{e.Id},{1},{now},{lease},{1L},{1})
            """);
        var repeated = Guid.NewGuid();
        var ordinal = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.IntentoBienvenida (Id,EventoId,Ordinal,ReservadoEnUtc,PropietarioLeaseId,LeaseGeneracion,Estado)
            VALUES ({repeated},{e.Id},{1},{now},{lease},{1L},{1})
            """));
        Assert.Contains(ordinal.Number, new[] { 2601, 2627 });
        Assert.Equal(1, await db.Set<EventoBienvenida>().CountAsync()); Assert.Equal(1, await db.Set<IntentoBienvenida>().CountAsync());
    }

    [C3ASqlFact]
    public async Task PrimeraConfirmacion_PersisteUsuarioConsumoYEventoJuntos()
    {
        await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock = new Clock(); var a = await Alta(d, clock); var quota = await Cuota(d);
        await Confirmar(d, clock, a);
        await using var db = d.CreateContext();
        var u = await db.Set<Usuario>().SingleAsync(); var t = await db.Set<DesafioVerificacionCorreo>().SingleAsync();
        var e = await db.Set<EventoBienvenida>().SingleAsync();
        Assert.Equal(EstadoVerificacionCorreo.Verificado, u.EstadoVerificacionCorreo);
        Assert.Equal(clock.Now, u.EmailVerificadoEnUtc); Assert.Equal(clock.Now, t.ConsumidoEnUtc);
        Assert.Equal(a.U.Id, e.UsuarioId); Assert.Equal(Epoch, e.EpocaCaptura); Assert.Equal(a.U.Email, e.Destinatario);
        Assert.Equal(clock.Now, e.ConfirmadoEnUtc); Assert.Equal(e.CapturadoEnUtc.AddHours(24), e.ExpiraEnUtc);
        Assert.Equal(TimeSpan.Zero, e.CapturadoEnUtc.Offset); Assert.Equal(EstadoEventoBienvenida.Pendiente, e.Estado);
        Assert.Equal(0, e.Reservas); Assert.Null(e.LeaseId); Assert.Null(e.PayloadUtf8); Assert.Null(e.PayloadSha256);
        Assert.Empty(await db.Set<IntentoBienvenida>().ToArrayAsync()); Assert.Equal(quota, await Cuota(d));

        // El esquema histórico canónico limita Usuario.Email a nvarchar(450).
        // Se comprueba el round-trip largo de la outbox sin alterar esa tabla ni fingir una confirmación imposible.
        var other = new Usuario("Histórico", $"{Guid.NewGuid()}@example.invalid", Hasher.HashPassword(Password), "Etc/UTC");
        db.Add(other); await db.SaveChangesAsync();
        var longAddress = "  Histórico <" + new string('x', 600) + "@example.invalid>  ";
        var historical = EventoBienvenida.CrearPendiente(other.Id, Epoch, longAddress, clock.Now, clock.Now);
        db.Add(historical); await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(longAddress, (await db.Set<EventoBienvenida>().SingleAsync(x => x.Id == historical.Id)).Destinatario);
        Assert.Equal(other.Email, (await db.Set<Usuario>().SingleAsync(x => x.Id == other.Id)).Email);
    }

    [C3ASqlFact]
    public async Task ConfirmacionRepetida_NoCreaOtroEvento()
    {
        await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock = new Clock(); var a = await Alta(d, clock); await Confirmar(d, clock, a);
        await using var db = d.CreateContext();
        var original = await db.Set<EventoBienvenida>().AsNoTracking().SingleAsync();
        Assert.Equal(ResultadoConfirmacionCorreo.Usado, await Repo(db, clock).ConfirmarAsync(a.Id, a.T.Hash, Password, Policy, default));
        Assert.Equal(original.Id, (await db.Set<EventoBienvenida>().SingleAsync()).Id);
    }

    [C3ASqlFact]
    public async Task ConfirmacionesConcurrentes_UnGanadorYUnEvento()
    {
        await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock = new Clock(); var a = await Alta(d, clock);
        async Task<ResultadoConfirmacionCorreo> Run()
        {
            await using var db = d.CreateContext(); return await Repo(db, clock).ConfirmarAsync(a.Id, a.T.Hash, Password, Policy, default);
        }
        var results = await Task.WhenAll(Run(), Run());
        Assert.Single(results, r => r == ResultadoConfirmacionCorreo.Confirmado);
        Assert.Single(results, r => r == ResultadoConfirmacionCorreo.Usado);
        await using var verify = d.CreateContext(); Assert.Equal(1, await verify.Set<EventoBienvenida>().CountAsync());
        Assert.NotNull((await verify.Set<DesafioVerificacionCorreo>().SingleAsync()).ConsumidoEnUtc);
    }

    [C3ASqlFact]
    public async Task ConfirmacionesInvalidasVencidasOReemplazadas_NoCapturan()
    {
        await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock = new Clock(); var a = await Alta(d, clock);
        var incompatible = OpcionesCapturaBienvenida.DesdeValores("true", Guid.NewGuid().ToString());
        await using (var db = d.CreateContext())
        {
            Assert.Equal(ResultadoConfirmacionCorreo.Invalido, await Repo(db, clock, incompatible).ConfirmarAsync(a.Id, a.T.Hash, "incorrecta", Policy, default));
            var t = await db.Set<DesafioVerificacionCorreo>().AsNoTracking().SingleAsync();
            Assert.Equal(1, t.IntentosConfirmacion); Assert.Null(t.ConsumidoEnUtc);
        }
        clock.Now = clock.Now.AddSeconds(60);
        await using (var db = d.CreateContext())
        {
            var next = new GeneradorTokenVerificacion().Generar();
            Assert.NotNull(await Repo(db, clock).ReenviarAsync(a.U.Email, next.Hash, Policy, default));
            Assert.NotEqual(ResultadoConfirmacionCorreo.Confirmado, await Repo(db, clock).ConfirmarAsync(a.Id, a.T.Hash, Password, Policy, default));
            clock.Now = clock.Now.AddHours(24);
            var active = await db.Set<DesafioVerificacionCorreo>().AsNoTracking().SingleAsync(x => x.InvalidadoEnUtc == null);
            Assert.NotEqual(ResultadoConfirmacionCorreo.Confirmado, await Repo(db, clock).ConfirmarAsync(active.Id, next.Hash, Password, Policy, default));
            Assert.Empty(await db.Set<EventoBienvenida>().ToArrayAsync());
            Assert.Equal(EstadoVerificacionCorreo.Pendiente, (await db.Set<Usuario>().SingleAsync()).EstadoVerificacionCorreo);
        }
    }

    [C3ASqlFact]
    public async Task FalloTrasSaveAntesDelCommit_RevierteTodo()
    {
        await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock = new Clock(); var a = await Alta(d, clock); var quota = await Cuota(d);
        await using (var db = d.CreateContext(new BeforeCommitFailure()))
            await Assert.ThrowsAsync<CorreoNoDisponibleException>(() => Repo(db, clock).ConfirmarAsync(a.Id, a.T.Hash, Password, Policy, default));
        await SinTransicion(d); Assert.Equal(quota, await Cuota(d));
        await using var verify = d.CreateContext(); Assert.Equal(0, (await verify.Set<DesafioVerificacionCorreo>().SingleAsync()).IntentosConfirmacion);
    }

    [C3ASqlFact]
    public async Task CommitRealConRespuestaPerdida_ConservaEventoYNoDuplicaAlRepetir()
    {
        await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock = new Clock(); var a = await Alta(d, clock); var failure = new AfterCommitFailure();
        await using (var db = d.CreateContext(failure))
            await Assert.ThrowsAsync<CorreoNoDisponibleException>(() => Repo(db, clock).ConfirmarAsync(a.Id, a.T.Hash, Password, Policy, default));
        Assert.Equal(1, failure.Calls);
        await using var verify = d.CreateContext();
        Assert.Equal(EstadoVerificacionCorreo.Verificado, (await verify.Set<Usuario>().SingleAsync()).EstadoVerificacionCorreo);
        Assert.NotNull((await verify.Set<DesafioVerificacionCorreo>().SingleAsync()).ConsumidoEnUtc);
        var id = (await verify.Set<EventoBienvenida>().AsNoTracking().SingleAsync()).Id;
        Assert.Equal(ResultadoConfirmacionCorreo.Usado, await Repo(verify, clock).ConfirmarAsync(a.Id, a.T.Hash, Password, Policy, default));
        Assert.Equal(id, (await verify.Set<EventoBienvenida>().SingleAsync()).Id);
    }

    [C3ASqlFact]
    public async Task CapturaDesactivada_ConfirmaConYSinEsquemaC3()
    {
        foreach (var migrated in new[] { false, true })
        {
            await using var d = await EmailVerificationSqlDatabase.CreateAsync();
            await using (var db = d.CreateContext())
                if (migrated) await db.Database.MigrateAsync(); else await db.GetService<IMigrator>().MigrateAsync(Previous);
            var clock = new Clock(); var a = await Alta(d, clock);
            await using var verify = d.CreateContext();
            Assert.Equal(ResultadoConfirmacionCorreo.Confirmado, await Repo(verify, clock, new()).ConfirmarAsync(a.Id, a.T.Hash, Password, Policy, default));
            Assert.Equal(EstadoVerificacionCorreo.Verificado, (await verify.Set<Usuario>().SingleAsync()).EstadoVerificacionCorreo);
            if (migrated) Assert.Empty(await verify.Set<EventoBienvenida>().ToArrayAsync());
        }
    }

    [C3ASqlFact]
    public async Task EpocaDesalineadaOConfiguracionInvalida_NoPersisteConfirmacion()
    {
        foreach (var options in new[] { OpcionesCapturaBienvenida.DesdeValores("true", Guid.NewGuid().ToString()), OpcionesCapturaBienvenida.DesdeValores("true", "invalida"), new OpcionesCapturaBienvenida() })
        {
            await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
            var clock = new Clock(); var a = await Alta(d, clock);
            await using (var db = d.CreateContext())
                await Assert.ThrowsAsync<CorreoNoDisponibleException>(() => Repo(db, clock, options).ConfirmarAsync(a.Id, a.T.Hash, Password, Policy, default));
            await SinTransicion(d);
            await using var verify = d.CreateContext(); Assert.Equal(0, (await verify.Set<DesafioVerificacionCorreo>().SingleAsync()).IntentosConfirmacion);
        }
    }

    [C3ASqlFact]
    public async Task EsquemaIncompletoOControlAusente_NoPersisteConfirmacion()
    {
        const string removeHistory = "DELETE dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]AddWelcomeEmailOutbox';";
        const string dropChildFk = "ALTER TABLE dbo.IntentoBienvenida DROP CONSTRAINT FK_IntentoBienvenida_EventoBienvenida_EventoId;";
        var damages = new[]
        {
            "DELETE dbo.ControlBienvenida",
            "DROP INDEX UX_IntentoBienvenida_EventoId_Ordinal ON dbo.IntentoBienvenida",
            "ALTER TABLE dbo.EventoBienvenida NOCHECK CONSTRAINT CK_Bienvenida_Estado",
            // Tablas completas sin evidencia de migración.
            removeHistory,
            // Metadatos de migración presentes, pero una o todas las tablas ausentes.
            "DROP TABLE dbo.ControlBienvenida;",
            "DROP TABLE dbo.IntentoBienvenida;",
            dropChildFk + "DROP TABLE dbo.EventoBienvenida;",
            "DROP TABLE dbo.IntentoBienvenida; DROP TABLE dbo.EventoBienvenida; DROP TABLE dbo.ControlBienvenida;",
            // Sin control ni historial: Evento/Intento no significan ausencia completa.
            removeHistory + "DROP TABLE dbo.ControlBienvenida;",
            // Cada tabla aislada, sin historial, también es una instalación parcial.
            removeHistory + "DROP TABLE dbo.IntentoBienvenida; DROP TABLE dbo.EventoBienvenida;",
            removeHistory + "DROP TABLE dbo.IntentoBienvenida; DROP TABLE dbo.ControlBienvenida;",
            removeHistory + dropChildFk + "DROP TABLE dbo.EventoBienvenida; DROP TABLE dbo.ControlBienvenida;"
        };
        foreach (var enabled in new[] { false, true })
        foreach (var damage in damages)
        {
            await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d, enabled);
            var clock = new Clock(); var a = await Alta(d, clock); var quota = await Cuota(d);
            var options = enabled ? Enabled : new OpcionesCapturaBienvenida();
            string[] history;
            await using (var db = d.CreateContext())
            {
                await db.Database.ExecuteSqlRawAsync(damage);
                history = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            }
            // La incompatibilidad no suprime el intento fallido que B debe persistir.
            await using (var db = d.CreateContext())
                Assert.Equal(ResultadoConfirmacionCorreo.Invalido,
                    await Repo(db, clock, options).ConfirmarAsync(a.Id, a.T.Hash, "incorrecta", Policy, default));
            await using (var db = d.CreateContext())
                await Assert.ThrowsAsync<CorreoNoDisponibleException>(() =>
                    Repo(db, clock, options).ConfirmarAsync(a.Id, a.T.Hash, Password, Policy, default));
            await using var verify = d.CreateContext();
            Assert.Equal(EstadoVerificacionCorreo.Pendiente, (await verify.Set<Usuario>().SingleAsync()).EstadoVerificacionCorreo);
            var challenge = await verify.Set<DesafioVerificacionCorreo>().SingleAsync();
            Assert.Null(challenge.ConsumidoEnUtc); Assert.Equal(1, challenge.IntentosConfirmacion);
            Assert.Equal(history, (await verify.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Equal(quota, await Cuota(d));
            if (await verify.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.EventoBienvenida',N'U') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync() == 1)
                Assert.Empty(await verify.Set<EventoBienvenida>().ToArrayAsync());
        }
    }

    [C3ASqlFact]
    public async Task CambioDeEpoca_EsperaConfirmacionEnCurso()
    {
        await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock = new Clock(); var a = await Alta(d, clock); var barrier = new ControlBarrier();
        await using var first = d.CreateContext(barrier);
        var confirmation = Repo(first, clock).ConfirmarAsync(a.Id, a.T.Hash, Password, Policy, default);
        var result = ResultadoConfirmacionCorreo.Invalido;
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(confirmation.IsCompleted);
            Assert.NotNull(first.Database.CurrentTransaction);
            Assert.Equal(System.Data.IsolationLevel.Serializable,
                Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions
                    .GetDbTransaction(first.Database.CurrentTransaction!).IsolationLevel);
            await using var other = d.CreateContext();
            var next = Guid.NewGuid();
            // SET y UPDATE comparten un batch/sesion; restaurar tambien al propagar el error SQL.
            var ex = await Assert.ThrowsAsync<SqlException>(() => other.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @previousLockTimeout int = @@LOCK_TIMEOUT;
                BEGIN TRY
                    SET LOCK_TIMEOUT 1000;
                    UPDATE dbo.ControlBienvenida SET EpocaCaptura={next} WHERE Id=1;
                    SET LOCK_TIMEOUT @previousLockTimeout;
                END TRY
                BEGIN CATCH
                    SET LOCK_TIMEOUT @previousLockTimeout;
                    THROW;
                END CATCH
                """));
            Assert.Equal(1222, ex.Number);
            Assert.False(confirmation.IsCompleted);
        }
        finally
        {
            barrier.Release.TrySetResult();
            // Observar siempre la tarea antes de disponer su contexto, incluso si falla una asercion.
            result = await confirmation;
        }
        Assert.Equal(ResultadoConfirmacionCorreo.Confirmado, result);
        await using var verify = d.CreateContext();
        var epoch = Guid.NewGuid();
        Assert.Equal(1, await verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.ControlBienvenida SET EpocaCaptura={epoch} WHERE Id=1"));
        Assert.Equal(Epoch, (await verify.Set<EventoBienvenida>().SingleAsync()).EpocaCaptura);
    }

    [C3ASqlFact]
    public async Task DownVacio_RestauraBConDatosPreviosIntactos()
    {
        await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d, false);
        var clock = new Clock(); var a = await Alta(d, clock);
        await using var db = d.CreateContext();
        var before = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        await db.GetService<IMigrator>().MigrateAsync(Previous);
        Assert.Equal(before[..^1], (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(a.U.Id, (await db.Set<Usuario>().SingleAsync()).Id);
        Assert.Equal(a.Id, (await db.Set<DesafioVerificacionCorreo>().SingleAsync()).Id);
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('EventoBienvenida','IntentoBienvenida','ControlBienvenida')").SingleAsync());
    }

    [C3ASqlFact]
    public async Task DownConEvidenciaOActivacion_Rechaza51040SinPerdidaParcial()
    {
        foreach (var evidence in new[] { false, true })
        {
            await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
            var clock = new Clock(); var a = await Alta(d, clock);
            if (evidence) await Confirmar(d, clock, a);
            await using var db = d.CreateContext();
            // Apagar flags no borra la evidencia de época activada.
            await db.Database.ExecuteSqlRawAsync("UPDATE dbo.ControlBienvenida SET CapturaHabilitada=0,FinCapturaEnUtc=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00') WHERE Id=1");
            var history = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            var ex = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync(Previous));
            Assert.Equal(51040, ex.Number);
            Assert.Equal(history, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Equal(evidence ? 1 : 0, await db.Set<EventoBienvenida>().CountAsync());
            Assert.Equal(a.U.Id, (await db.Set<Usuario>().SingleAsync()).Id);
            Assert.Equal(1, await db.Set<ControlBienvenida>().CountAsync());
            Assert.Empty(await db.Set<IntentoBienvenida>().ToArrayAsync());
            Assert.Equal(evidence, (await db.Set<DesafioVerificacionCorreo>().SingleAsync()).ConsumidoEnUtc.HasValue);
        }
    }

    [C3ASqlFact]
    public async Task Retencion_DepuraDatosPeroConservaDeduplicacion()
    {
        await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock = new Clock(); var a = await Alta(d, clock); await Confirmar(d, clock, a);
        await using var db = d.CreateContext();
        var e = await db.Set<EventoBienvenida>().AsNoTracking().SingleAsync();
        var old = DateTimeOffset.UtcNow.AddDays(-32); var finalized = old.AddHours(1); var lease = Guid.NewGuid(); var attempt = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET CapturadoEnUtc={old},ExpiraEnUtc={old.AddHours(24)},
            Estado=7,MotivoFinal=1,FinalizadoEnUtc={finalized},ProximaEjecucionEnUtc=NULL,
            Reservas=1,PrimeraReservaEnUtc={old},HorizonteHttpEnUtc={old.AddHours(23)},PayloadUtf8=0x01,
            PayloadSha256={new byte[32]},PerfilProveedorVersion='test-v1',IdProveedor='test-id',UltimoLeaseHastaUtc={old}
            WHERE Id={e.Id};
            INSERT dbo.IntentoBienvenida (Id,EventoId,Ordinal,ReservadoEnUtc,PropietarioLeaseId,LeaseGeneracion,Estado)
            VALUES ({attempt},{e.Id},{1},{old},{lease},{1L},{1})
            """);
        Assert.Equal(1, await new RetencionBienvenidaRepository(db).AplicarAsync(100, default));
        var after = await db.Set<EventoBienvenida>().AsNoTracking().SingleAsync();
        Assert.Equal(e.Id, after.Id); Assert.Equal(e.UsuarioId, after.UsuarioId); Assert.Equal(e.ClaveIdempotencia, after.ClaveIdempotencia);
        Assert.Null(after.Destinatario); Assert.Null(after.PayloadUtf8); Assert.Null(after.PayloadSha256); Assert.Null(after.IdProveedor);
        Assert.NotNull(after.DatosDepuradosEnUtc); Assert.Empty(await db.Set<IntentoBienvenida>().ToArrayAsync());
        Assert.Equal(0, await new RetencionBienvenidaRepository(db).AplicarAsync(100, default));
        db.Add(EventoBienvenida.CrearPendiente(a.U.Id, Epoch, a.U.Email, clock.Now, clock.Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [C3ASqlFact]
    public async Task Retencion_RespetaLeasesVentanasYPendientesNoVencidos()
    {
        await using var d = await EmailVerificationSqlDatabase.CreateAsync(); await Init(d);
        var clock = new Clock(); var a = await Alta(d, clock); await Confirmar(d, clock, a);
        await using var db = d.CreateContext();
        var e = await db.Set<EventoBienvenida>().AsNoTracking().SingleAsync();
        var repo = new RetencionBienvenidaRepository(db);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repo.AplicarAsync(101, default));
        Assert.Equal(0, await repo.AplicarAsync(100, default)); // Pendiente aún vigente.
        var now = DateTimeOffset.UtcNow; var lease = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET Estado=2,LeaseId={lease},LeaseGeneracion=1,
            LeaseHastaUtc={now.AddSeconds(90)},UltimoLeaseHastaUtc={now.AddSeconds(90)} WHERE Id={e.Id}
            """);
        Assert.Equal(0, await repo.AplicarAsync(100, default));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET Estado=7,MotivoFinal=3,FinalizadoEnUtc=CapturadoEnUtc,
            ProximaEjecucionEnUtc=NULL,LeaseId=NULL,LeaseHastaUtc=NULL WHERE Id={e.Id}
            """);
        Assert.Equal(0, await repo.AplicarAsync(100, default)); // Lease + diez minutos.
        var old = now.AddDays(-32); var recent = now.AddHours(-1); var attempt = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET CapturadoEnUtc={old},ExpiraEnUtc={old.AddHours(24)},FinalizadoEnUtc={old.AddHours(1)},
            UltimoLeaseHastaUtc={old},Reservas=1,PrimeraReservaEnUtc={old},HorizonteHttpEnUtc={old.AddHours(23)},
            PayloadUtf8=0x01,PayloadSha256={new byte[32]},PerfilProveedorVersion='test-v1' WHERE Id={e.Id};
            INSERT dbo.IntentoBienvenida (Id,EventoId,Ordinal,ReservadoEnUtc,PropietarioLeaseId,LeaseGeneracion,Estado)
            VALUES ({attempt},{e.Id},{1},{recent},{lease},{1L},{1})
            """);
        Assert.Equal(1, await repo.AplicarAsync(1, default)); // Depura PII, conserva intento dentro de cuota.
        Assert.Equal(attempt, (await db.Set<IntentoBienvenida>().SingleAsync()).Id);
        Assert.NotNull((await db.Set<EventoBienvenida>().AsNoTracking().SingleAsync()).DatosDepuradosEnUtc);
        Assert.Equal(0, await repo.AplicarAsync(100, default));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.IntentoBienvenida SET ReservadoEnUtc={old} WHERE Id={attempt}");
        Assert.Equal(1, await repo.AplicarAsync(100, default)); Assert.Empty(await db.Set<IntentoBienvenida>().ToArrayAsync());
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-22T10:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class SimulatedDbFailure : DbException { }
    private sealed class BeforeCommitFailure : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new SimulatedDbFailure();
    }
    private sealed class AfterCommitFailure : DbTransactionInterceptor
    {
        public int Calls;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Calls++; throw new SimulatedDbFailure();
        }
    }
    private sealed class ControlBarrier : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [dbo].[ControlBienvenida] WITH (HOLDLOCK)", StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
    private sealed class C3ASqlFactAttribute : FactAttribute
    {
        public C3ASqlFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
                || Environment.GetEnvironmentVariable("CONSTIA_AUTHEMAIL01C3A_SQLSERVER_TESTS") != "true"
                || Environment.GetEnvironmentVariable("CONSTIA_AUTHEMAIL01B_SQLSERVER_TESTS") != "true")
                Skip = "Requires the independently enabled, identity-verified disposable C3-A SQL CI service.";
        }
    }
}
