using Constia.API.Contracts;
using Constia.Application.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace Constia.API.Controllers;

[ApiController]
[Route("api/usuarios")]
public sealed class UsuariosController(RegistrarUsuario registrarUsuario) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(RegistroUsuarioResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegistroUsuarioResponse>> Registrar(
        RegistroUsuarioRequest request,
        CancellationToken cancellationToken)
    {
        var usuario = await registrarUsuario.EjecutarAsync(
            request.Nombre,
            request.Email,
            request.Password,
            cancellationToken);

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
}
