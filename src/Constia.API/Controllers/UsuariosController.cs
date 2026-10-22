using Constia.API.Contracts;
using Constia.API.Authentication;
using Constia.Application.Habitos;
using Constia.Application.Temporal;
using Constia.Application.Usuarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Constia.API.Controllers;

[ApiController]
[Route("api/usuarios")]
public sealed class UsuariosController(
    RegistrarUsuario registrarUsuario,
    CambiarZonaHorariaUsuario cambiarZonaHorariaUsuario,
    IUsuarioActual usuarioActual) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("correo")]
    [ProducesResponseType(typeof(RegistroUsuarioResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegistroUsuarioResponse>> Registrar(
        RegistroUsuarioRequest request,
        CancellationToken cancellationToken)
    {
        UsuarioRegistrado? usuario;
        try
        {
            usuario = await registrarUsuario.EjecutarAsync(
                request.Nombre,
                request.Email,
                request.Password,
                request.TimeZoneId,
                cancellationToken);
        }
        catch (CorreoNoDisponibleException)
        {
            return StatusCode(503, new ProblemDetails
            {
                Status = 503, Title = "El registro no está disponible temporalmente.",
                Extensions = { ["code"] = "registration_unavailable" }
            });
        }
        catch (Constia.Application.Correo.DatosCorreoInvalidosException exception)
        {
            ModelState.AddModelError(exception.Campo, "El dato no es válido para el registro.");
            return ValidationProblem(ModelState);
        }
        catch (ContrasenaInvalidaException exception)
        {
            foreach (var error in exception.Errores)
            {
                ModelState.AddModelError(nameof(RegistroUsuarioRequest.Password), error);
            }
            return ValidationProblem(ModelState);
        }
        catch (ZonaHorariaIanaInvalidaException)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "No se pudo registrar el usuario.",
                Detail = "El identificador de zona horaria IANA no es válido o no está disponible."
            });
        }
        if (usuario is null)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "No se pudo registrar el usuario.",
                Detail = "El email ya está registrado."
            });
        }

        var response = new RegistroUsuarioResponse(usuario.Id, usuario.Nombre, usuario.Email);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [Authorize]
    [HttpPut("mi-zona-horaria")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CambiarZonaHoraria(
        CambiarZonaHorariaRequest request,
        CancellationToken cancellationToken)
    {
        if (usuarioActual.UsuarioId is not Guid usuarioId)
        {
            return Unauthorized();
        }

        try
        {
            var actualizado = await cambiarZonaHorariaUsuario.EjecutarAsync(
                usuarioId,
                request.TimeZoneId,
                cancellationToken);

            return actualizado ? NoContent() : Unauthorized();
        }
        catch (ZonaHorariaIanaInvalidaException)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "No se pudo cambiar la zona horaria.",
                Detail = "El identificador de zona horaria IANA no es válido o no está disponible."
            });
        }
        catch (HistorialEstadoHabitoNoInicializadoException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "El historial de estados todavía no está disponible.",
                Extensions = { ["code"] = "habit_status_history_not_initialized" }
            });
        }
    }
}
