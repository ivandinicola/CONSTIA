using Constia.Application.Habitos;
using Constia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Constia.Infrastructure.Habitos;

public sealed class HabitoRepository(ConstiaDbContext dbContext) : IHabitoRepository
{
    public async Task AgregarAsync(Habito habito, CancellationToken cancellationToken)
    {
        dbContext.Set<Habito>().Add(habito);
        dbContext.Entry(habito).Property<long>("ConcurrencyVersion").CurrentValue = 1;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Habito>> ListarActivosPorUsuarioAsync(
        Guid usuarioId,
        CancellationToken cancellationToken)
    {
        return await HabitosConProgramaciones()
            .Where(habito =>
                habito.Usuario.Id == usuarioId &&
                habito.Estado == EstadoHabito.Activo)
            .AsNoTracking()
            .OrderBy(habito => habito.FechaCreacion)
            .ThenBy(habito => habito.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Habito>> ListarInactivosPorUsuarioAsync(
        Guid usuarioId,
        CancellationToken cancellationToken)
    {
        return await HabitosConProgramaciones()
            .Where(habito =>
                habito.Usuario.Id == usuarioId &&
                habito.Estado == EstadoHabito.Inactivo)
            .AsNoTracking()
            .OrderBy(habito => habito.FechaCreacion)
            .ThenBy(habito => habito.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<Habito?> BuscarPorIdYUsuarioAsync(
        Guid habitId,
        Guid usuarioId,
        CancellationToken cancellationToken)
    {
        return HabitosConProgramaciones(includeStateHistory: true)
            .Where(habito =>
                habito.Id == habitId &&
                EF.Property<Guid>(habito, "UsuarioId") == usuarioId)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<Habito?> BuscarParaModificarPorIdYUsuarioAsync(
        Guid habitId,
        Guid usuarioId,
        CancellationToken cancellationToken)
    {
        return HabitosConProgramaciones(includeOwner: true, includeStateHistory: true)
            .Where(habito =>
                habito.Id == habitId &&
                EF.Property<Guid>(habito, "UsuarioId") == usuarioId)
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task GuardarCambiosAsync(Habito habito, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(habito);

        var entry = dbContext.Entry(habito);
        if (entry.State is EntityState.Detached or EntityState.Added or EntityState.Deleted)
        {
            throw new InvalidOperationException("El hábito debe estar cargado y rastreado para guardar cambios.");
        }

        var version = entry.Property<long>("ConcurrencyVersion");
        version.CurrentValue = checked(version.OriginalValue + 1);
        version.IsModified = true;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception) when (
            exception.Entries.Any(entry => entry.Entity is Habito entity && entity.Id == habito.Id))
        {
            throw new ConflictoEdicionConcurrenteException(
                "The habit was changed by another operation.",
                exception);
        }
    }

    private IQueryable<Habito> HabitosConProgramaciones(
        bool includeOwner = false,
        bool includeStateHistory = false)
    {
        IQueryable<Habito> query = dbContext.Set<Habito>()
            .Include(habito => habito.Programaciones)
            .ThenInclude(programacion => programacion.DiasProgramados);

        if (includeOwner)
        {
            query = query.Include(habito => habito.Usuario);
        }

        if (includeStateHistory)
        {
            query = query.Include(habito => habito.PeriodosEstado);
        }

        return query;
    }
}
