using Constia.Application.Cumplimientos;
using Constia.Domain;

namespace Constia.Application.Habitos;

public sealed class ConsultarRachas(
    IHabitosDiariosReadRepository diarios,
    IHabitoRepository habitos,
    IHistorialCumplimientosRepository historial,
    IEstadoHabitoReadinessRepository readiness)
{
    public async Task<ResultadoRachas> GeneralAsync(Guid usuarioId, DateOnly corte, CancellationToken ct)
    {
        await VerificarHistorialAsync(ct);

        var candidatos = await diarios.ListarCandidatosAsync(usuarioId, corte, ct);
        if (candidatos.Count == 0)
            return CalcularRachas.General(corte, []);

        var desde = candidatos.Min(habito => habito.FechaInicio);
        var registros = await diarios.ListarCumplidosAsync(usuarioId, desde, corte, ct);
        return CalcularRachas.General(corte, DiasGenerales(candidatos, registros, desde, corte, ct));
    }

    public async Task<ResultadoRachas?> IndividualAsync(Guid usuarioId, Guid id, DateOnly corte, CancellationToken ct)
    {
        var habito = await habitos.BuscarPorIdYUsuarioAsync(id, usuarioId, ct);
        if (habito is null)
            return null;

        await VerificarHistorialAsync(ct);
        var activo = habito.Estado == EstadoHabito.Activo;
        if (habito.FechaInicio > corte)
            return CalcularRachas.Individual(corte, activo, []);

        var registros = await historial.ListarFechasAsync(id, usuarioId, habito.FechaInicio, corte, ct);
        return CalcularRachas.Individual(corte, activo, DiasIndividuales(habito, registros, corte, ct));
    }

    private async Task VerificarHistorialAsync(CancellationToken ct)
    {
        if (!await readiness.HistorialCompletoAsync(ct))
            throw new HistorialEstadoHabitoNoInicializadoException();
    }

    private static IEnumerable<DiaRachaGeneral> DiasGenerales(
        IReadOnlyList<Habito> candidatos, IReadOnlySet<(Guid HabitoId, DateOnly Fecha)> registros,
        DateOnly desde, DateOnly corte, CancellationToken ct)
    {
        for (var numero = desde.DayNumber; numero <= corte.DayNumber; numero++)
        {
            ct.ThrowIfCancellationRequested();
            var fecha = DateOnly.FromDayNumber(numero);
            var programados = 0;
            var cumplidos = 0;
            var desconocidosConRegistro = 0;
            var desconocidosSinRegistro = 0;
            foreach (var habito in candidatos)
            {
                var oportunidad = OportunidadHabito.Evaluar(habito, fecha);
                if (oportunidad == false)
                    continue;

                var tieneRegistro = registros.Contains((habito.Id, fecha));
                if (oportunidad == true)
                {
                    programados++;
                    if (tieneRegistro)
                        cumplidos++;
                }
                else if (tieneRegistro)
                    desconocidosConRegistro++;
                else
                    desconocidosSinRegistro++;
            }

            yield return new(fecha, programados, cumplidos, desconocidosConRegistro, desconocidosSinRegistro);
        }
    }

    private static IEnumerable<DiaRachaIndividual> DiasIndividuales(
        Habito habito, IReadOnlySet<DateOnly> registros, DateOnly corte, CancellationToken ct)
    {
        for (var numero = habito.FechaInicio.DayNumber; numero <= corte.DayNumber; numero++)
        {
            ct.ThrowIfCancellationRequested();
            var fecha = DateOnly.FromDayNumber(numero);
            var oportunidad = OportunidadHabito.Evaluar(habito, fecha);
            if (oportunidad != false)
                yield return new(fecha, oportunidad, registros.Contains(fecha));
        }
    }
}
