using Constia.Application.Cumplimientos;
using Constia.Domain;

namespace Constia.Application.Habitos;

internal static class OportunidadHabito
{
    public static bool AplicaA(Habito habito, DateOnly fecha) =>
        Evaluar(habito, fecha) ?? throw new EstadoHabitoHistoricoDesconocidoException();

    public static bool? Evaluar(Habito habito, DateOnly fecha)
    {
        var programacion = habito.ObtenerProgramacionPara(fecha);
        if (programacion is null || !programacion.DiasProgramados.Any(dia => dia.Dia == fecha.DayOfWeek))
        {
            return false;
        }

        return habito.ObtenerEstadoPara(fecha) is { } estado ? estado == EstadoHabito.Activo : null;
    }
}
