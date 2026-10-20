using Constia.Domain;
using Constia.Application.Temporal;
using Constia.Application.Usuarios;

namespace Constia.Application.Habitos;

public sealed class EditarHabito(
    IHabitoRepository habitos,
    IUsuarioRepository usuarios,
    IFechaLocalUsuario fechaLocalUsuario,
    ICategoriaHabitoReadRepository categorias)
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

        if (request.CategoriaId is Guid categoria &&
            (!CatalogoCategoriasHabito.EsOficial(categoria) ||
             !await categorias.ExisteAsync(categoria, cancellationToken)))
            throw new MetadatosHabitoInvalidosException("categoriaId", "La categoría no es oficial o no está disponible.");

        if (request.IconoId is not null && !CatalogoIconosHabito.EsOficial(request.IconoId))
            throw new MetadatosHabitoInvalidosException("iconoId", "El ícono debe ser una clave oficial exacta.");

        var fechaEfectiva = fechaLocalUsuario.ObtenerHoy(usuario.TimeZoneId);
        habito.ActualizarConfiguracionVersionada(
            request.Nombre,
            request.Descripcion,
            request.DiasProgramados,
            fechaEfectiva);
        habito.ActualizarMetadatos(request.CategoriaId, request.IconoId);
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
