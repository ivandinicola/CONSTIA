using Constia.API.Authentication;
using Constia.API.Contracts;
using Constia.Application.Cumplimientos;
using Constia.Application.Habitos;
using Constia.Application.Temporal;
using Constia.Domain;
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
    ListarCategoriasHabito listarCategoriasHabito,
    ObtenerHabitoPorId obtenerHabitoPorId,
    EditarHabito editarHabito,
    DesactivarHabito desactivarHabito,
    RegistrarCumplimiento registrarCumplimiento) : ControllerBase
{
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
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

        try
        {
            var desactivado = await desactivarHabito.EjecutarAsync(id, usuarioId, cancellationToken);
            return desactivado ? NoContent() : NotFound();
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
        catch (ConflictoVigenciaEstadoHabitoException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "No se pudo desactivar el hábito por un conflicto de vigencia."
            });
        }
        catch (ConflictoEdicionConcurrenteException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "El hábito cambió durante la operación. Volvé a intentarlo."
            });
        }
        catch (ZonaHorariaPersistidaInvalidaException)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "No se pudo determinar la fecha local del usuario.");
        }
    }

    [HttpGet("categorias")]
    [ProducesResponseType(typeof(IReadOnlyList<CategoriaHabitoListado>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<CategoriaHabitoListado>>> ListarCategorias(
        CancellationToken cancellationToken)
    {
        var categorias = await listarCategoriasHabito.EjecutarAsync(cancellationToken);
        return Ok(categorias);
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
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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
        catch (ConflictoVigenciaProgramacionException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "No se pudo actualizar la programación del hábito.",
                Detail = "La fecha efectiva entra en conflicto con la programación existente."
            });
        }
        catch (ConflictoEdicionConcurrenteException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "No se pudo actualizar el hábito.",
                Detail = "El hábito cambió durante la operación. Volvé a intentarlo."
            });
        }
        catch (ZonaHorariaPersistidaInvalidaException)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "No se pudo determinar la fecha local del usuario.");
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

    [HttpPost("{id:guid}/cumplimientos")]
    [ProducesResponseType(typeof(CumplimientoResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CumplimientoResponse>> RegistrarCumplimiento(
        Guid id,
        RegistrarCumplimientoHttpRequest request,
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
            var registrado = await registrarCumplimiento.EjecutarAsync(
                id,
                usuarioId,
                request.Fecha!.Value,
                cancellationToken);

            if (registrado is null)
            {
                return NotFound();
            }

            return StatusCode(StatusCodes.Status201Created, new CumplimientoResponse(
                registrado.Id,
                registrado.Fecha));
        }
        catch (UsuarioAutenticadoNoEncontradoException)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "No se pudo identificar al usuario autenticado."
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
        catch (EstadoHabitoHistoricoDesconocidoException)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "No se conoce el estado histórico del hábito para esa fecha.",
                Extensions = { ["code"] = "habit_status_history_unknown" }
            });
        }
        catch (ConflictoRegistroCumplimientoException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "No se pudo registrar el cumplimiento.",
                Detail = "El hábito no admite otro cumplimiento para esa fecha."
            });
        }
        catch (ZonaHorariaPersistidaInvalidaException)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "No se pudo determinar la fecha local del usuario.");
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Los datos del cumplimiento no son válidos.",
                Detail = exception.Message
            });
        }
    }
    [HttpPost]
    [ProducesResponseType(typeof(HabitoResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
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
