using Constia.API.Authentication;
using Constia.API.Contracts;
using Constia.Application.Habitos;
using Constia.Application.Temporal;
using Constia.Application.Usuarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Constia.API.Controllers;

[ApiController]
[Route("api/habitos")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RachasController(
    IUsuarioActual usuarioActual,
    IUsuarioRepository usuarios,
    IFechaLocalUsuario fechaLocal,
    ConsultarRachas consulta) : ControllerBase
{
    [HttpGet("rachas")]
    [ProducesResponseType<RachasResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public Task<ActionResult<RachasResponse>> General(CancellationToken ct) => ConsultarAsync(null, ct);

    [HttpGet("{id:guid}/rachas")]
    [ProducesResponseType<RachasResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public Task<ActionResult<RachasResponse>> Individual(Guid id, CancellationToken ct) => ConsultarAsync(id, ct);

    private async Task<ActionResult<RachasResponse>> ConsultarAsync(Guid? id, CancellationToken ct)
    {
        if (usuarioActual.UsuarioId is not Guid usuarioId || await usuarios.BuscarPorIdAsync(usuarioId, ct) is not { } usuario)
            return Unauthorized();

        try
        {
            var corte = fechaLocal.ObtenerHoy(usuario.TimeZoneId);
            var resultado = id is Guid habitoId
                ? await consulta.IndividualAsync(usuarioId, habitoId, corte, ct)
                : await consulta.GeneralAsync(usuarioId, corte, ct);
            if (resultado is not { } rachas)
                return NotFound();

            if (rachas.Actual.Estado != EstadoMetricaRacha.Disponible &&
                rachas.Mejor.Estado != EstadoMetricaRacha.Disponible)
            {
                return Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "El historial de estados no permite determinar las rachas.",
                    extensions: new Dictionary<string, object?> { ["code"] = "habit_status_history_unknown" });
            }

            return Ok(new RachasResponse(corte, Mapear(rachas.Actual), Mapear(rachas.Mejor)));
        }
        catch (HistorialEstadoHabitoNoInicializadoException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "El historial de estados todavía no está disponible.",
                extensions: new Dictionary<string, object?> { ["code"] = "habit_status_history_not_initialized" });
        }
        catch (ZonaHorariaPersistidaInvalidaException)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "No se pudo determinar la fecha local del usuario.");
        }
    }

    private static MetricaRachaResponse Mapear(MetricaRacha metrica) => new(metrica.Estado switch
    {
        EstadoMetricaRacha.Disponible => "disponible",
        EstadoMetricaRacha.NoAplicable => "no_aplicable",
        EstadoMetricaRacha.NoDisponible => "no_disponible",
        _ => throw new ArgumentOutOfRangeException(nameof(metrica))
    }, metrica.Valor);
}
