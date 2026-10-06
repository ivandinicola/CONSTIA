using Constia.Application.Usuarios;
using Constia.Domain;

namespace Constia.Application.Habitos;

public sealed class CrearHabito(
    IUsuarioRepository usuarios,
    IHabitoRepository habitos)
{
    /// <summary>
    /// Returns null when the owner does not exist.
    /// </summary>
    public async Task<HabitoCreado?> EjecutarAsync(
        CrearHabitoRequest solicitud,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var usuario = await usuarios.BuscarPorIdAsync(solicitud.UsuarioId, cancellationToken);
        if (usuario is null)
        {
            return null;
        }

        var habito = new Habito(
            usuario,
            solicitud.Nombre,
            solicitud.Descripcion,
            solicitud.FechaInicio,
            solicitud.DiasProgramados);

        await habitos.AgregarAsync(habito, cancellationToken);

        return new HabitoCreado(
            habito.Id,
            usuario.Id,
            habito.Nombre,
            habito.Descripcion,
            habito.FechaCreacion,
            habito.FechaInicio,
            habito.Estado,
            habito.DiasProgramados.Select(dia => dia.Dia).ToArray());
    }
}
