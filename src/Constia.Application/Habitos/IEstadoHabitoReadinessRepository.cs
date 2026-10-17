namespace Constia.Application.Habitos;

/// <summary>Reads the global gates required to safely use historical habit state.</summary>
public interface IEstadoHabitoReadinessRepository
{
    Task<bool> SnapshotCapturadoAsync(CancellationToken cancellationToken);

    Task<bool> HistorialCompletoAsync(CancellationToken cancellationToken);
}
