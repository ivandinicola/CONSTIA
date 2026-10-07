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
        return await dbContext.Set<Habito>()
            .Where(habito =>
                habito.Usuario.Id == usuarioId &&
                habito.Estado == EstadoHabito.Activo)
            .Include(habito => habito.DiasProgramados)
            .AsNoTracking()
            .OrderBy(habito => habito.FechaCreacion)
            .ThenBy(habito => habito.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Habito>> ListarInactivosPorUsuarioAsync(
        Guid usuarioId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<Habito>()
            .Where(habito =>
                habito.Usuario.Id == usuarioId &&
                habito.Estado == EstadoHabito.Inactivo)
            .Include(habito => habito.DiasProgramados)
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
        return dbContext.Set<Habito>()
            .Where(habito =>
                habito.Id == habitId &&
                EF.Property<Guid>(habito, "UsuarioId") == usuarioId)
            .Include(habito => habito.DiasProgramados)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<Habito?> BuscarParaModificarPorIdYUsuarioAsync(
        Guid habitId,
        Guid usuarioId,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<Habito>()
            .Where(habito =>
                habito.Id == habitId &&
                EF.Property<Guid>(habito, "UsuarioId") == usuarioId)
            .Include(habito => habito.DiasProgramados)
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task GuardarCambiosAsync(CancellationToken cancellationToken)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
