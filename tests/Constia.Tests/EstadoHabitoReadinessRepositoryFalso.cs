using Constia.Application.Habitos;

namespace Constia.Tests;

internal sealed class EstadoHabitoReadinessRepositoryFalso(
    bool snapshotCapturado = true,
    bool historialCompleto = true) : IEstadoHabitoReadinessRepository
{
    public Task<bool> SnapshotCapturadoAsync(CancellationToken cancellationToken) =>
        Task.FromResult(snapshotCapturado);

    public Task<bool> HistorialCompletoAsync(CancellationToken cancellationToken) =>
        Task.FromResult(historialCompleto);
}
