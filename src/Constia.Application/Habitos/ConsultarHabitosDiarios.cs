using Constia.Application.Cumplimientos;
using Constia.Domain;

namespace Constia.Application.Habitos;

public sealed class ConsultarHabitosDiarios(
    IHabitosDiariosReadRepository habitos,
    IEstadoHabitoReadinessRepository readiness)
{
    public async Task<IReadOnlyList<HabitoDiario>> EjecutarAsync(
        Guid usuarioId, DateOnly fecha, CancellationToken cancellationToken)
    {
        if (!await readiness.HistorialCompletoAsync(cancellationToken))
        {
            throw new HistorialEstadoHabitoNoInicializadoException();
        }

        var candidatos = await habitos.ListarCandidatosAsync(usuarioId, fecha, cancellationToken);
        var activos = new List<Habito>();
        foreach (var habito in candidatos)
        {
            var programacion = habito.ObtenerProgramacionPara(fecha);
            if (programacion is null || !programacion.DiasProgramados.Any(dia => dia.Dia == fecha.DayOfWeek))
            {
                continue;
            }

            var estado = habito.ObtenerEstadoPara(fecha)
                ?? throw new EstadoHabitoHistoricoDesconocidoException();
            if (estado == EstadoHabito.Activo)
            {
                activos.Add(habito);
            }
        }

        if (activos.Count == 0)
        {
            return [];
        }

        var cumplidos = await habitos.ListarIdsCumplidosAsync(usuarioId, fecha, cancellationToken);
        return activos.Select(habito => new HabitoDiario(
            habito.Id, habito.Nombre, habito.Descripcion, habito.CategoriaId,
            habito.IconoId, cumplidos.Contains(habito.Id))).ToArray();
    }
}

public sealed record HabitoDiario(
    Guid Id, string Nombre, string? Descripcion, Guid? CategoriaId, string? IconoId, bool Cumplido);
