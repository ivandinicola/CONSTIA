using Constia.Application.Habitos;
using Microsoft.EntityFrameworkCore;

namespace Constia.Infrastructure.Habitos;

public sealed class EstadoHabitoReadinessRepository(ConstiaDbContext dbContext)
    : IEstadoHabitoReadinessRepository
{
    public async Task<bool> SnapshotCapturadoAsync(CancellationToken cancellationToken)
    {
        var marker = await dbContext.InicializacionesEstadoHabito
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == InicializacionEstadoHabito.RegistroUnicoId, cancellationToken);

        return marker?.SnapshotCapturedAtUtc is not null;
    }

    public async Task<bool> HistorialCompletoAsync(CancellationToken cancellationToken)
    {
        var marker = await dbContext.InicializacionesEstadoHabito
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == InicializacionEstadoHabito.RegistroUnicoId, cancellationToken);

        return marker?.CompletedAtUtc is not null;
    }
}
