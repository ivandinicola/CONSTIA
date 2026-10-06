namespace Constia.Application.Habitos;

public sealed class ObtenerHabitoPorId(IHabitoRepository habitos)
{
    public async Task<HabitoListado?> EjecutarAsync(
        Guid habitId,
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        var habito = await habitos.BuscarPorIdYUsuarioAsync(habitId, usuarioId, cancellationToken);

        return habito is null
            ? null
            : new HabitoListado(
                habito.Id,
                habito.Nombre,
                habito.Descripcion,
                habito.FechaCreacion,
                habito.FechaInicio,
                habito.Estado,
                habito.DiasProgramados.Select(dia => dia.Dia).ToArray());
    }
}
