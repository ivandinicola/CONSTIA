using System.Data;
using System.Data.Common;
using Constia.Application.Usuarios;
using Constia.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Constia.Infrastructure.Usuarios;

/// <summary>Operaciones especializadas; bloqueos y mutaciones pertenecen a la misma transacción/contexto.</summary>
public sealed class VerificacionCorreoRepository(ConstiaDbContext db, IUsuarioPasswordHasher hasher, TimeProvider clock)
    : IVerificacionCorreoRepository
{
    public async Task<bool> EsquemaDisponibleAsync(CancellationToken ct)
    {
        try
        {
            return await db.Database.SqlQueryRaw<int>("""
                SELECT CASE WHEN
                    OBJECT_ID(N'dbo.DesafioVerificacionCorreo',N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.PresupuestoEmisionCorreo',N'U') IS NOT NULL
                    AND EXISTS (SELECT 1 FROM dbo.PresupuestoEmisionCorreo WHERE Id = 1 AND TotalEmisiones >= 0)
                    AND COL_LENGTH(N'dbo.Usuario',N'EstadoVerificacionCorreo') IS NOT NULL
                    AND COL_LENGTH(N'dbo.Usuario',N'EmailVerificadoEnUtc') IS NOT NULL
                    AND COL_LENGTH(N'dbo.Usuario',N'VersionCorreo') IS NOT NULL
                    AND EXISTS (SELECT 1 FROM sys.triggers WHERE object_id = OBJECT_ID(N'dbo.TR_Usuario_ProtegeVerificacionCorreo') AND is_disabled = 0)
                    AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Usuario_VerificacionCorreo' AND is_disabled = 0 AND is_not_trusted = 0)
                    AND EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20261021062129_AddEmailVerification')
                THEN 1 ELSE 0 END AS [Value]
                """).SingleAsync(ct) == 1;
        }
        catch (DbException) { return false; }
    }

    public async Task<ReservaRegistroCorreo> RegistrarAsync(Usuario usuario, byte[] hash, PoliticaVerificacionCorreo politica, CancellationToken ct)
    {
        if (!politica.EsValida || usuario.EstadoVerificacionCorreo != EstadoVerificacionCorreo.Pendiente)
            throw new CorreoNoDisponibleException();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            // Serializar primero la identidad/email, después la fila global. Mismo orden en reenvío.
            if (await PorEmailBloqueado(usuario.Email).AnyAsync(ct))
                return new(EstadoReservaCorreo.Duplicado);
            var presupuesto = await PresupuestoBloqueado().SingleAsync(ct);
            await db.Entry(presupuesto).ReloadAsync(ct);
            var now = clock.GetUtcNow();
            if (!await HayPresupuestoAsync(now, politica, ct))
                return new(EstadoReservaCorreo.PresupuestoAgotado);
            var desafio = new DesafioVerificacionCorreo(usuario.Id, usuario.Email, hash, now);
            db.Set<Usuario>().Add(usuario);
            db.Set<DesafioVerificacionCorreo>().Add(desafio);
            presupuesto.Reservar();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(EstadoReservaCorreo.Creado, new(desafio.Id, usuario.Email));
        }
        catch (DbUpdateException ex) when (DuplicadoEmail(ex))
        {
            db.ChangeTracker.Clear();
            return new(EstadoReservaCorreo.Duplicado);
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            db.ChangeTracker.Clear();
            throw new CorreoNoDisponibleException();
        }
    }

    public async Task<EmisionCorreo?> ReenviarAsync(string email, byte[] hash, PoliticaVerificacionCorreo politica, CancellationToken ct)
    {
        if (!politica.EsValida) throw new CorreoNoDisponibleException();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var usuario = await PorEmailBloqueado(email).SingleOrDefaultAsync(ct);
            if (usuario is not null) await db.Entry(usuario).ReloadAsync(ct);
            if (usuario is null || usuario.EstadoVerificacionCorreo != EstadoVerificacionCorreo.Pendiente)
                return null;
            var presupuesto = await PresupuestoBloqueado().SingleAsync(ct);
            await db.Entry(presupuesto).ReloadAsync(ct);
            var now = clock.GetUtcNow();
            var emisiones = await db.Set<DesafioVerificacionCorreo>().Where(d => d.UsuarioId == usuario.Id && d.Proposito == 1)
                .OrderByDescending(d => d.EmitidoEnUtc).Take(politica.EmisionesPorUsuario)
                .Select(d => d.EmitidoEnUtc).ToArrayAsync(ct);
            if (!politica.PermiteEmision(emisiones, now) || !await HayPresupuestoAsync(now, politica, ct))
                return null;
            var anterior = await db.Set<DesafioVerificacionCorreo>()
                .SingleOrDefaultAsync(d => d.UsuarioId == usuario.Id && d.Proposito == 1 && d.ConsumidoEnUtc == null && d.InvalidadoEnUtc == null, ct);
            if (anterior is not null)
            {
                await db.Entry(anterior).ReloadAsync(ct);
                anterior.Invalidar(now);
                // Evitar depender del orden insert/update elegido por EF para el índice único filtrado.
                await db.SaveChangesAsync(ct);
            }
            var nuevo = new DesafioVerificacionCorreo(usuario.Id, usuario.Email, hash, now);
            db.Set<DesafioVerificacionCorreo>().Add(nuevo);
            presupuesto.Reservar();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(nuevo.Id, usuario.Email);
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            db.ChangeTracker.Clear();
            throw new CorreoNoDisponibleException();
        }
    }

    public async Task<ResultadoConfirmacionCorreo> ConfirmarAsync(Guid desafioId, byte[] hash, string password, PoliticaVerificacionCorreo politica, CancellationToken ct)
    {
        if (!politica.EsValida) return ResultadoConfirmacionCorreo.Invalido;
        try
        {
            // Las filas históricas no se borran: obtener la identidad antes del bloqueo no consume el desafío.
            var propietario = await db.Set<DesafioVerificacionCorreo>().AsNoTracking()
                .Where(d => d.Id == desafioId).Select(d => (Guid?)d.UsuarioId).SingleOrDefaultAsync(ct);
            if (propietario is null) return ResultadoConfirmacionCorreo.Invalido;
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var usuario = await db.Set<Usuario>().FromSqlInterpolated(
                $"SELECT * FROM [Usuario] WITH (UPDLOCK,HOLDLOCK) WHERE [Id] = {propietario.Value}")
                .SingleOrDefaultAsync(ct);
            if (usuario is not null) await db.Entry(usuario).ReloadAsync(ct);
            var desafio = await db.Set<DesafioVerificacionCorreo>().SingleAsync(d => d.Id == desafioId, ct);
            await db.Entry(desafio).ReloadAsync(ct);
            var now = clock.GetUtcNow();
            if (desafio.ConsumidoEnUtc is not null) return ResultadoConfirmacionCorreo.Usado;
            if (desafio.InvalidadoEnUtc is not null) return ResultadoConfirmacionCorreo.Reemplazado;
            if (!desafio.EstaVigente(now)) return ResultadoConfirmacionCorreo.Vencido;
            if (!desafio.RegistrarIntento(now, politica.IntentosPorDesafio, politica.VentanaIntentos))
                return ResultadoConfirmacionCorreo.LimiteIntentos;
            var valido = usuario is not null && usuario.EstadoVerificacionCorreo == EstadoVerificacionCorreo.Pendiente
                && desafio.UsuarioId == usuario.Id && desafio.Proposito == 1
                && string.Equals(desafio.EmailDestino, usuario.Email, StringComparison.Ordinal)
                && HashTokenVerificacion.Coincide(desafio.TokenHash, hash)
                && hasher.VerifyPassword(usuario.PasswordHash, password);
            if (valido)
            {
                usuario!.ConfirmarCorreo(now);
                desafio.Consumir(now);
            }
            // También se guardan los intentos rechazados, sin consumir el token ni modificar la contraseña.
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return valido ? ResultadoConfirmacionCorreo.Confirmado : ResultadoConfirmacionCorreo.Invalido;
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            db.ChangeTracker.Clear();
            throw new CorreoNoDisponibleException();
        }
    }

    private IQueryable<Usuario> PorEmailBloqueado(string email) =>
        db.Set<Usuario>().FromSqlInterpolated($"SELECT * FROM [Usuario] WITH (UPDLOCK,HOLDLOCK) WHERE [Email] = {email}");

    private IQueryable<PresupuestoEmisionCorreo> PresupuestoBloqueado() =>
        db.Set<PresupuestoEmisionCorreo>().FromSqlRaw("SELECT * FROM [PresupuestoEmisionCorreo] WITH (UPDLOCK,HOLDLOCK) WHERE [Id] = 1");

    private async Task<bool> HayPresupuestoAsync(DateTimeOffset now, PoliticaVerificacionCorreo p, CancellationToken ct)
    {
        var desde = now.AddHours(-24);
        // Las emisiones iniciales y reemplazadas permanecen en el mismo historial persistente.
        return await db.Set<DesafioVerificacionCorreo>().CountAsync(d => d.EmitidoEnUtc > desde, ct) < p.EmisionesGlobales;
    }

    private static bool DuplicadoEmail(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && sql.Errors.Cast<SqlError>()
            .Any(e => e.Number is 2601 or 2627 && e.Message.Contains("IX_Usuario_Email", StringComparison.OrdinalIgnoreCase));
}
