using Constia.Domain;

namespace Constia.Application.Habitos;

public sealed record HabitoListado(
    Guid Id,
    string Nombre,
    string? Descripcion,
    DateTimeOffset FechaCreacion,
    DateOnly FechaInicio,
    EstadoHabito Estado,
    IReadOnlyList<DayOfWeek> DiasProgramados);
