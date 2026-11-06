namespace Constia.Application.Cumplimientos;

public interface IHistorialCumplimientosRepository
{
    // null indica que no existe un hábito con ese ID para el propietario.
    Task<IReadOnlyList<CumplimientoListado>?> ListarPorHabitoYUsuarioAsync(
        Guid habitoId, Guid usuarioId, CancellationToken cancellationToken);
}

public sealed record CumplimientoListado(Guid Id, DateOnly Fecha);
