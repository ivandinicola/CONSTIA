using Microsoft.EntityFrameworkCore;

namespace Constia.Infrastructure.Correo;

/// <summary>Persistencia aislada: nunca envía correo ni activa trabajadores.</summary>
public sealed class AutorizacionEnvioUnicoRepository(ConstiaDbContext db, TimeProvider clock)
{
    public async Task<bool> ConsumirAsync(Guid ensayo, byte[] identidad, Guid operacion, CancellationToken ct)
    {
        if (ensayo == Guid.Empty || operacion == Guid.Empty || identidad is not { Length: 32 }) return false;
        identidad = (byte[])identidad.Clone();
        try
        {
            if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null || !db.Database.IsSqlServer()) return false;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = clock.GetUtcNow();
            var filas = await db.Set<AutorizacionEnvioUnico>()
                .Where(x => x.EnsayoId == ensayo && x.OperacionId == operacion && x.Estado == EstadoAutorizacionEnvio.Preparada && EF.Property<byte[]>(x, "_identidadDestinatario") == identidad)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, EstadoAutorizacionEnvio.Consumida).SetProperty(x => x.ConsumidaEnUtc, now), ct);
            if (filas != 1) return false;
            await tx.CommitAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Un commit incierto jamás autoriza una operación posterior.
            return false;
        }
    }
    public async Task<bool> RegistrarResultadoAsync(Guid ensayo, Guid operacion, EstadoAutorizacionEnvio resultado, CancellationToken ct)
    {
        if (ensayo == Guid.Empty || operacion == Guid.Empty || resultado is not (EstadoAutorizacionEnvio.Aceptada or EstadoAutorizacionEnvio.Incierta)) return false;
        try
        {
            if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null || !db.Database.IsSqlServer()) return false;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = clock.GetUtcNow();
            var filas = await db.Set<AutorizacionEnvioUnico>()
                .Where(x => x.EnsayoId == ensayo && x.OperacionId == operacion && x.Estado == EstadoAutorizacionEnvio.Consumida)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, resultado).SetProperty(x => x.ResultadoEnUtc, now), ct);
            if (filas != 1) return false;
            await tx.CommitAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { return false; }
    }
}