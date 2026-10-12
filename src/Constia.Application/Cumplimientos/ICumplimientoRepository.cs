using Constia.Domain;

namespace Constia.Application.Cumplimientos;

public interface ICumplimientoRepository
{
    Task<bool> ExistePorHabitoYFechaAsync(
        Guid habitId,
        DateOnly fecha,
        CancellationToken cancellationToken);

    Task AgregarAsync(Cumplimiento cumplimiento, CancellationToken cancellationToken);
}
