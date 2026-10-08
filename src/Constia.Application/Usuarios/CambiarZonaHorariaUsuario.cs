using Constia.Application.Temporal;

namespace Constia.Application.Usuarios;

public sealed class CambiarZonaHorariaUsuario(
    IUsuarioRepository usuarios,
    IZonaHorariaIana zonasHorarias)
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

        usuario.CambiarZonaHoraria(timeZoneId);
        await usuarios.GuardarCambiosAsync(cancellationToken);
        return true;
    }
}
