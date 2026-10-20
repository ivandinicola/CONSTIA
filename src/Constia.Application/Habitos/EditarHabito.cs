using Constia.Application.Temporal;
using Constia.Application.Usuarios;

namespace Constia.Application.Habitos;

public sealed class EditarHabito(
    IHabitoRepository habitos,
    IUsuarioRepository usuarios,
    IFechaLocalUsuario fechaLocalUsuario)
{
    public async Task<HabitoListado?> EjecutarAsync(
        Guid habitId,
        Guid usuarioId,
        EditarHabitoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var habito = await habitos.BuscarParaModificarPorIdYUsuarioAsync(
            habitId,
            usuarioId,
            cancellationToken);
        if (habito is null)
        {
            return null;
        }

        var usuario = await usuarios.BuscarPorIdAsync(usuarioId, cancellationToken);
        if (usuario is null)
        {
            return null;
        }

        var fechaEfectiva = fechaLocalUsuario.ObtenerHoy(usuario.TimeZoneId);
        habito.ActualizarConfiguracionVersionada(
            request.Nombre,
            request.Descripcion,
            request.DiasProgramados,
            fechaEfectiva);
        await habitos.GuardarCambiosAsync(habito, cancellationToken);

        return new HabitoListado(
            habito.Id,
            habito.Nombre,
            habito.Descripcion,
            habito.FechaCreacion,
            habito.FechaInicio,
            habito.Estado,
            habito.DiasProgramados.Select(dia => dia.Dia).ToArray(),
            habito.CategoriaId,
            habito.IconoId);
    }
}
