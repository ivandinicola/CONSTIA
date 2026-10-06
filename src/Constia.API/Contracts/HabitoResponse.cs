using Constia.Domain;

namespace Constia.API.Contracts;

public sealed record HabitoResponse(
    Guid Id,
    string Nombre,
    string? Descripcion,
    DateTimeOffset CreatedAt,
    DateOnly StartDate,
    EstadoHabito Estado,
    IReadOnlyList<DayOfWeek> DiasProgramados);
