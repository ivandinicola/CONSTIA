using Constia.Application.Cumplimientos;
using Constia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Constia.Infrastructure.Cumplimientos;

public sealed class CumplimientoRepository(ConstiaDbContext dbContext) : ICumplimientoRepository
{
    public Task<bool> ExistePorHabitoYFechaAsync(
        Guid habitId,
        DateOnly fecha,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<Cumplimiento>()
            .AsNoTracking()
            .AnyAsync(cumplimiento =>
                EF.Property<Guid>(cumplimiento, "HabitoId") == habitId &&
                cumplimiento.Fecha == fecha,
                cancellationToken);
    }

    public async Task AgregarAsync(Cumplimiento cumplimiento, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cumplimiento);
        dbContext.Set<Cumplimiento>().Add(cumplimiento);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
