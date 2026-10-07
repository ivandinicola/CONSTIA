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
    CrearHabito crearHabito,
    ListarHabitosActivos listarHabitosActivos,
    ListarHabitosInactivos listarHabitosInactivos,
    ObtenerHabitoPorId obtenerHabitoPorId,
    EditarHabito editarHabito,
    DesactivarHabito desactivarHabito) : ControllerBase
{
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Desactivar(
        Guid id,
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

        var desactivado = await desactivarHabito.EjecutarAsync(id, usuarioId, cancellationToken);
        return desactivado ? NoContent() : NotFound();
    }

    [HttpGet("inactivos")]
    [ProducesResponseType(typeof(IReadOnlyList<HabitoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<HabitoResponse>>> ListarInactivos(
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

        var habitos = await listarHabitosInactivos.EjecutarAsync(usuarioId, cancellationToken);
        var response = habitos.Select(habito => new HabitoResponse(
            habito.Id,
            habito.Nombre,
            habito.Descripcion,
            habito.FechaCreacion,
            habito.FechaInicio,
            habito.Estado,
            habito.DiasProgramados)).ToArray();

        return Ok(response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(HabitoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HabitoResponse>> Editar(
        Guid id,
        EditarHabitoHttpRequest request,
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
            var editado = await editarHabito.EjecutarAsync(
                id,
                usuarioId,
                new EditarHabitoRequest(
                    request.Nombre,
                    request.Descripcion,
                    request.DiasProgramados!),
                cancellationToken);

            if (editado is null)
            {
                return NotFound();
            }

            return Ok(new HabitoResponse(
                editado.Id,
                editado.Nombre,
                editado.Descripcion,
                editado.FechaCreacion,
                editado.FechaInicio,
                editado.Estado,
                editado.DiasProgramados));
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

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(HabitoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HabitoResponse>> ObtenerPorId(
        Guid id,
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

        var habito = await obtenerHabitoPorId.EjecutarAsync(id, usuarioId, cancellationToken);
        if (habito is null)
        {
            return NotFound();
        }

        return Ok(new HabitoResponse(
            habito.Id,
            habito.Nombre,
            habito.Descripcion,
            habito.FechaCreacion,
            habito.FechaInicio,
            habito.Estado,
            habito.DiasProgramados));
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<HabitoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<HabitoResponse>>> ListarActivos(
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

        var habitos = await listarHabitosActivos.EjecutarAsync(usuarioId, cancellationToken);
        var response = habitos.Select(habito => new HabitoResponse(
            habito.Id,
            habito.Nombre,
            habito.Descripcion,
            habito.FechaCreacion,
            habito.FechaInicio,
            habito.Estado,
            habito.DiasProgramados)).ToArray();

        return Ok(response);
    }

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
