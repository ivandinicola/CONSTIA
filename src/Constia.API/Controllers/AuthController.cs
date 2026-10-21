using Constia.API.Contracts;
using Constia.Application.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace Constia.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AutenticarUsuario autenticarUsuario) : ControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginUsuarioResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LoginUsuarioResponse>> Login(
        LoginUsuarioRequest request,
        CancellationToken cancellationToken)
    {
        var resultado = await autenticarUsuario.EjecutarAsync(
            request.Email,
            request.Password,
            cancellationToken);

        if (resultado.Estado == EstadoAutenticacion.CredencialesInvalidas)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "No se pudo iniciar sesión.",
                Detail = "Las credenciales no son válidas."
            });
        }

        if (resultado.Estado == EstadoAutenticacion.VerificacionRequerida)
            return StatusCode(403, new ProblemDetails
            {
                Status = 403, Title = "Verificá tu correo para iniciar sesión.",
                Extensions = { ["code"] = "email_verification_required" }
            });

        var usuario = resultado.Usuario!;
        return Ok(new LoginUsuarioResponse(
            usuario.AccessToken,
            usuario.ExpiresAt,
            usuario.UserId,
            usuario.Name,
            usuario.Email));
    }
}
