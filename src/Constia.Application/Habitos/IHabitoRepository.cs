using Constia.Domain;

namespace Constia.Application.Habitos;

public interface IHabitoRepository
{
    Task AgregarAsync(Habito habito, CancellationToken cancellationToken);

    Task<IReadOnlyList<Habito>> ListarActivosPorUsuarioAsync(
        Guid usuarioId,
        CancellationToken cancellationToken);

    Task<Habito?> BuscarPorIdYUsuarioAsync(
        Guid habitId,
        Guid usuarioId,
        CancellationToken cancellationToken);
}
