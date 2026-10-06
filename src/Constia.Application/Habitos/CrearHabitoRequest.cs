namespace Constia.Application.Habitos;

public sealed record CrearHabitoRequest(
    Guid UsuarioId,
    string Nombre,
    string? Descripcion,
    DateOnly FechaInicio,
    IReadOnlyCollection<DayOfWeek> DiasProgramados);
