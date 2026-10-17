using Constia.Application.Temporal;
using Constia.Application.Habitos;

namespace Constia.Application.Usuarios;

public sealed class CambiarZonaHorariaUsuario(
    IUsuarioRepository usuarios,
    IZonaHorariaIana zonasHorarias,
    IEstadoHabitoReadinessRepository readiness)
{
    public async Task<bool> EjecutarAsync(
        Guid usuarioId,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        if (!zonasHorarias.EsValida(timeZoneId))
        {
            throw new ZonaHorariaIanaInvalidaException(timeZoneId);
        }

        var usuario = await usuarios.BuscarPorIdAsync(usuarioId, cancellationToken);
        if (usuario is null)
        {
            return false;
        }

        if (!await readiness.SnapshotCapturadoAsync(cancellationToken))
        {
            throw new HistorialEstadoHabitoNoInicializadoException();
        }

        usuario.CambiarZonaHoraria(timeZoneId);
        await usuarios.GuardarCambiosAsync(cancellationToken);
        return true;
    }
}
