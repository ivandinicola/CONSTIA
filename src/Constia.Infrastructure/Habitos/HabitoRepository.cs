using Constia.Application.Habitos;
using Constia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Constia.Infrastructure.Habitos;

public sealed class HabitoRepository(ConstiaDbContext dbContext) : IHabitoRepository
{
    public async Task AgregarAsync(Habito habito, CancellationToken cancellationToken)
    {
        dbContext.Set<Habito>().Add(habito);
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
        return HabitosConProgramaciones()
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
        return HabitosConProgramaciones()
            .Where(habito =>
                habito.Id == habitId &&
                EF.Property<Guid>(habito, "UsuarioId") == usuarioId)
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task GuardarCambiosAsync(CancellationToken cancellationToken)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<Habito> HabitosConProgramaciones() => dbContext.Set<Habito>()
        .Include(habito => habito.Programaciones)
        .ThenInclude(programacion => programacion.DiasProgramados);
}
