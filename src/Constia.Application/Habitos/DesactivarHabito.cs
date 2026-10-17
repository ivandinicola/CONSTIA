namespace Constia.Application.Habitos;

public sealed class DesactivarHabito(
    IHabitoRepository habitos,
    Constia.Application.Temporal.IFechaLocalUsuario fechaLocalUsuario,
    IEstadoHabitoReadinessRepository readiness)
{
    public async Task<bool> EjecutarAsync(
        Guid habitId,
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        var habito = await habitos.BuscarParaModificarPorIdYUsuarioAsync(
            habitId,
            usuarioId,
            cancellationToken);
        if (habito is null)
        {
            return false;
        }

        if (!await readiness.HistorialCompletoAsync(cancellationToken))
        {
            throw new HistorialEstadoHabitoNoInicializadoException();
        }

        var fechaEfectiva = fechaLocalUsuario.ObtenerHoy(habito.Usuario.TimeZoneId);
        if (!habito.Desactivar(fechaEfectiva))
        {
            return true;
        }

        await habitos.GuardarCambiosAsync(habito, cancellationToken);
        return true;
    }
}
