using Constia.Domain;

namespace Constia.Application.Habitos;

public interface IHabitosDiariosReadRepository
{
    Task<IReadOnlyList<Habito>> ListarCandidatosAsync(
        Guid usuarioId, DateOnly fecha, CancellationToken cancellationToken);

    Task<IReadOnlySet<(Guid HabitoId, DateOnly Fecha)>> ListarCumplidosAsync(
        Guid usuarioId, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken);
}
