namespace Constia.Application.Habitos;

public sealed class ConsultarCalendarioHabitos(
    IHabitosDiariosReadRepository habitos,
    IEstadoHabitoReadinessRepository readiness)
{
    public async Task<IReadOnlyList<DiaCalendario>> EjecutarAsync(
        Guid usuarioId, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken)
    {
        if (!await readiness.HistorialCompletoAsync(cancellationToken))
        {
            throw new HistorialEstadoHabitoNoInicializadoException();
        }

        var candidatos = await habitos.ListarCandidatosAsync(usuarioId, hasta, cancellationToken);
        var cumplimientos = await habitos.ListarCumplidosAsync(usuarioId, desde, hasta, cancellationToken);
        var dias = new List<DiaCalendario>();

        for (var numero = desde.DayNumber; numero <= hasta.DayNumber; numero++)
        {
            var fecha = DateOnly.FromDayNumber(numero);
            var programados = 0;
            var cumplidos = 0;
            foreach (var habito in candidatos)
            {
                if (!OportunidadHabito.AplicaA(habito, fecha))
                {
                    continue;
                }

                programados++;
                if (cumplimientos.Contains((habito.Id, fecha)))
                {
                    cumplidos++;
                }
            }

            dias.Add(new DiaCalendario(fecha, programados, cumplidos));
        }

        return dias;
    }
}

public sealed record DiaCalendario(DateOnly Fecha, int Programados, int Cumplidos);
