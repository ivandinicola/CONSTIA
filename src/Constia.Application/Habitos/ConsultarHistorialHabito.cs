using Constia.Application.Cumplimientos;

namespace Constia.Application.Habitos;

public sealed class ConsultarHistorialHabito(
    IHabitoRepository habitos,
    IHistorialCumplimientosRepository cumplimientos,
    IEstadoHabitoReadinessRepository readiness)
{
    public async Task<IReadOnlyList<DiaHistorialHabito>?> EjecutarAsync(
        Guid id, Guid usuarioId, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken)
    {
        var habito = await habitos.BuscarPorIdYUsuarioAsync(id, usuarioId, cancellationToken);
        if (habito is null)
        {
            return null;
        }

        if (!await readiness.HistorialCompletoAsync(cancellationToken))
        {
            throw new HistorialEstadoHabitoNoInicializadoException();
        }

        var registros = await cumplimientos.ListarFechasAsync(id, usuarioId, desde, hasta, cancellationToken);
        var dias = new List<DiaHistorialHabito>(hasta.DayNumber - desde.DayNumber + 1);
        for (var numero = desde.DayNumber; numero <= hasta.DayNumber; numero++)
        {
            var fecha = DateOnly.FromDayNumber(numero);
            var tieneRegistro = registros.Contains(fecha);
            var estado = OportunidadHabito.Evaluar(habito, fecha) switch
            {
                null => "desconocido",
                false => "no_programado",
                true => tieneRegistro ? "cumplido" : "pendiente"
            };
            dias.Add(new(fecha, estado, tieneRegistro));
        }
        return dias;
    }
}

public sealed record DiaHistorialHabito(DateOnly Fecha, string Estado, bool TieneRegistro);
