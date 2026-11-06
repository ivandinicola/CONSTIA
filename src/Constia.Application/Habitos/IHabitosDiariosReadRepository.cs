using Constia.Domain;

namespace Constia.Application.Habitos;

public interface IHabitosDiariosReadRepository
{
    Task<IReadOnlyList<Habito>> ListarCandidatosAsync(
        Guid usuarioId, DateOnly fecha, CancellationToken cancellationToken);

    Task<IReadOnlySet<Guid>> ListarIdsCumplidosAsync(
        Guid usuarioId, DateOnly fecha, CancellationToken cancellationToken);
}
