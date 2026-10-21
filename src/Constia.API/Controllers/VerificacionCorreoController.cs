using Constia.API.Contracts;
using Constia.Application.Usuarios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Constia.API.Controllers;

[ApiController]
[Route("api/auth/email")]
[EnableRateLimiting("correo")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class VerificacionCorreoController(ServicioVerificacionCorreo servicio) : ControllerBase
{
    [HttpPost("verificar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails),StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Confirmar(ConfirmarCorreoRequest request, CancellationToken ct)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        try
        {
            var resultado = await servicio.ConfirmarAsync(request.DesafioId, request.Token, request.Password, ct);
            return resultado == ResultadoConfirmacionCorreo.Confirmado ? NoContent() : BadRequest(new ProblemDetails
            {
                Status=400, Title="No se pudo confirmar el correo.",
                Detail="El enlace o los datos no son válidos. Podés solicitar un nuevo correo.",
                Extensions = { ["code"]="email_verification_invalid" }
            });
        }
        catch (CorreoNoDisponibleException) { return NoDisponible(); }
    }

    [HttpPost("reenviar")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Reenviar(ReenviarCorreoRequest request, CancellationToken ct)
    {
        try
        {
            await servicio.ReenviarAsync(request.Email, ct);
            return Accepted(new { mensaje="Si corresponde, recibirás un correo con los próximos pasos." });
        }
        catch (CorreoNoDisponibleException) { return NoDisponible(); }
    }

    private ObjectResult NoDisponible() => StatusCode(503,new ProblemDetails
    {
        Status=503, Title="La operación no está disponible temporalmente.",
        Extensions = { ["code"]="registration_unavailable" }
    });
}
