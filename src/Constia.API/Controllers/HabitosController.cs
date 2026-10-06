using Constia.API.Authentication;
using Constia.API.Contracts;
using Constia.Application.Habitos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Constia.API.Controllers;

[ApiController]
[Route("api/habitos")]
[Authorize]
public sealed class HabitosController(
    IUsuarioActual usuarioActual,
    CrearHabito crearHabito) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(HabitoResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HabitoResponse>> Crear(
        CrearHabitoHttpRequest request,
        CancellationToken cancellationToken)
    {
        if (usuarioActual.UsuarioId is not Guid usuarioId)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "No se pudo identificar al usuario autenticado."
            });
        }

        try
        {
            var creado = await crearHabito.EjecutarAsync(
                new CrearHabitoRequest(
                    usuarioId,
                    request.Nombre,
                    request.Descripcion,
                    request.FechaInicio!.Value,
                    request.DiasProgramados!),
                cancellationToken);

            if (creado is null)
            {
                return Unauthorized(new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "No se pudo identificar al usuario autenticado."
                });
            }

            return StatusCode(StatusCodes.Status201Created, new HabitoResponse(
                creado.Id,
                creado.Nombre,
                creado.Descripcion,
                creado.FechaCreacion,
                creado.FechaInicio,
                creado.Estado,
                creado.DiasProgramados));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Los datos del hábito no son válidos.",
                Detail = exception.Message
            });
        }
    }
}
