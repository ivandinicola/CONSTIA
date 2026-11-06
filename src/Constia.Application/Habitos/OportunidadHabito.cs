using Constia.Application.Cumplimientos;
using Constia.Domain;

namespace Constia.Application.Habitos;

internal static class OportunidadHabito
{
    public static bool AplicaA(Habito habito, DateOnly fecha)
    {
        var programacion = habito.ObtenerProgramacionPara(fecha);
        if (programacion is null || !programacion.DiasProgramados.Any(dia => dia.Dia == fecha.DayOfWeek))
        {
            return false;
        }

        var estado = habito.ObtenerEstadoPara(fecha)
            ?? throw new EstadoHabitoHistoricoDesconocidoException();
        return estado == EstadoHabito.Activo;
    }
}
