using Constia.Application.Habitos;
using Constia.Application.Temporal;
using Constia.Application.Usuarios;
using Constia.Domain;

namespace Constia.Application.Cumplimientos;

public sealed class RegistrarCumplimiento(
    IUsuarioRepository usuarios,
    IHabitoRepository habitos,
    ICumplimientoRepository cumplimientos,
    IFechaLocalUsuario fechaLocalUsuario,
    IEstadoHabitoReadinessRepository readiness)
{
    public async Task<CumplimientoRegistrado?> EjecutarAsync(
        Guid habitId,
        Guid usuarioId,
        DateOnly fecha,
        CancellationToken cancellationToken = default)
    {
        var usuario = await usuarios.BuscarPorIdAsync(usuarioId, cancellationToken);
        if (usuario is null)
        {
            throw new UsuarioAutenticadoNoEncontradoException();
        }

        var habito = await habitos.BuscarParaModificarPorIdYUsuarioAsync(
            habitId,
            usuarioId,
            cancellationToken);
        if (habito is null)
        {
            return null;
        }

        if (!await readiness.HistorialCompletoAsync(cancellationToken))
        {
            throw new HistorialEstadoHabitoNoInicializadoException();
        }

        var hoy = fechaLocalUsuario.ObtenerHoy(usuario.TimeZoneId);
        if (fecha != hoy && fecha != hoy.AddDays(-1))
        {
            throw new ArgumentException("Solo se permiten cumplimientos de hoy o ayer.", nameof(fecha));
        }

        if (fecha < habito.FechaInicio)
        {
            throw new ArgumentException("La fecha no puede ser anterior al inicio del hábito.", nameof(fecha));
        }

        var estadoHistorico = habito.ObtenerEstadoPara(fecha);
        if (estadoHistorico is null)
        {
            throw new EstadoHabitoHistoricoDesconocidoException();
        }

        if (habito.Estado != EstadoHabito.Activo)
        {
            throw new ConflictoRegistroCumplimientoException();
        }

        if (estadoHistorico != EstadoHabito.Activo)
        {
            throw new ConflictoRegistroCumplimientoException();
        }

        var programacion = habito.ObtenerProgramacionPara(fecha);
        if (programacion is null || !programacion.DiasProgramados.Any(dia => dia.Dia == fecha.DayOfWeek))
        {
            throw new ArgumentException("El hábito no estaba programado para esa fecha.", nameof(fecha));
        }

        if (await cumplimientos.ExistePorHabitoYFechaAsync(habitId, fecha, cancellationToken))
        {
            throw new ConflictoRegistroCumplimientoException();
        }

        var cumplimiento = new Cumplimiento(habito, fecha);
        await cumplimientos.AgregarAsync(cumplimiento, cancellationToken);

        return new CumplimientoRegistrado(cumplimiento.Id, cumplimiento.Fecha);
    }
}
