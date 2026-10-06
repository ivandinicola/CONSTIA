using Constia.Domain;

namespace Constia.Application.Habitos;

public sealed record HabitoCreado(
    Guid Id,
    Guid UsuarioId,
    string Nombre,
    string? Descripcion,
    DateTimeOffset FechaCreacion,
    DateOnly FechaInicio,
    EstadoHabito Estado,
    IReadOnlyList<DayOfWeek> DiasProgramados);
