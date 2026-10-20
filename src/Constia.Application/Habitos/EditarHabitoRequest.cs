namespace Constia.Application.Habitos;

public sealed record EditarHabitoRequest(
    string Nombre,
    string? Descripcion,
    IReadOnlyCollection<DayOfWeek> DiasProgramados,
    Guid? CategoriaId = null,
    string? IconoId = null);
