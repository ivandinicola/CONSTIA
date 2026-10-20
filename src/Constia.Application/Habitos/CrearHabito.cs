using Constia.Application.Usuarios;
using Constia.Application.Temporal;
using Constia.Domain;

namespace Constia.Application.Habitos;

public sealed class CrearHabito(
    IUsuarioRepository usuarios,
    IHabitoRepository habitos,
    IEstadoHabitoReadinessRepository readiness,
    ICategoriaHabitoReadRepository categorias,
    IFechaLocalUsuario fechaLocalUsuario,
    TimeProvider timeProvider)
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

        if (!await readiness.SnapshotCapturadoAsync(cancellationToken))
        {
            throw new HistorialEstadoHabitoNoInicializadoException();
        }

        if (!CatalogoCategoriasHabito.EsOficial(solicitud.CategoriaId) ||
            !await categorias.ExisteAsync(solicitud.CategoriaId, cancellationToken))
            throw new MetadatosHabitoInvalidosException("categoriaId", "La categoría no es oficial o no está disponible.");
        if (!CatalogoIconosHabito.EsOficial(solicitud.IconoId))
            throw new MetadatosHabitoInvalidosException("iconoId", "El ícono debe ser una clave oficial exacta.");

        ArgumentException.ThrowIfNullOrWhiteSpace(solicitud.Nombre);
        ArgumentNullException.ThrowIfNull(solicitud.DiasProgramados);
        // Copia estable antes de consultar el reloj o persistir el agregado.
        var dias = solicitud.DiasProgramados.ToArray();
        if (dias.Length == 0 || dias.Any(dia => !Enum.IsDefined(dia)))
            throw new ArgumentException("Debe seleccionarse al menos un día válido.", nameof(solicitud.DiasProgramados));

        var instanteUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var fechaInicio = fechaLocalUsuario.ObtenerFechaLocal(instanteUtc, usuario.TimeZoneId);
        var habito = new Habito(
            usuario,
            solicitud.Nombre,
            solicitud.Descripcion,
            instanteUtc,
            fechaInicio,
            solicitud.CategoriaId,
            solicitud.IconoId,
            dias);

        await habitos.AgregarAsync(habito, cancellationToken);

        return new HabitoCreado(
            habito.Id,
            usuario.Id,
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
