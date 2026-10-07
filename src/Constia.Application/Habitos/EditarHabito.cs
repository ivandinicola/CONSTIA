namespace Constia.Application.Habitos;

public sealed class EditarHabito(IHabitoRepository habitos)
{
    public async Task<HabitoListado?> EjecutarAsync(
        Guid habitId,
        Guid usuarioId,
        EditarHabitoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var habito = await habitos.BuscarParaEditarPorIdYUsuarioAsync(
            habitId,
            usuarioId,
            cancellationToken);
        if (habito is null)
        {
            return null;
        }

        habito.ActualizarConfiguracion(request.Nombre, request.Descripcion, request.DiasProgramados);
        await habitos.GuardarCambiosAsync(cancellationToken);

        return new HabitoListado(
            habito.Id,
            habito.Nombre,
            habito.Descripcion,
            habito.FechaCreacion,
            habito.FechaInicio,
            habito.Estado,
            habito.DiasProgramados.Select(dia => dia.Dia).ToArray());
    }
}
