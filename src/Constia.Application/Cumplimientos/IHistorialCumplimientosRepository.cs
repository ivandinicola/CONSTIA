namespace Constia.Application.Cumplimientos;

public interface IHistorialCumplimientosRepository
{
    Task<IReadOnlySet<DateOnly>> ListarFechasAsync(
        Guid habitoId, Guid usuarioId, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken);

    // null indica que no existe un hábito con ese ID para el propietario.
    Task<IReadOnlyList<CumplimientoListado>?> ListarPorHabitoYUsuarioAsync(
        Guid habitoId, Guid usuarioId, CancellationToken cancellationToken);
}

public sealed record CumplimientoListado(Guid Id, DateOnly Fecha);
