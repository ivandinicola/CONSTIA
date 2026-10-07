namespace Constia.Application.Habitos;

public sealed class ListarHabitosInactivos(IHabitoRepository habitos)
{
    public async Task<IReadOnlyList<HabitoListado>> EjecutarAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        var resultados = await habitos.ListarInactivosPorUsuarioAsync(usuarioId, cancellationToken);

        return resultados
            .Select(habito => new HabitoListado(
                habito.Id,
                habito.Nombre,
                habito.Descripcion,
                habito.FechaCreacion,
                habito.FechaInicio,
                habito.Estado,
                habito.DiasProgramados.Select(dia => dia.Dia).ToArray()))
            .ToArray();
    }
}
