namespace Constia.Application.Habitos;

public sealed record CrearHabitoRequest(
    Guid UsuarioId,
    string Nombre,
    string? Descripcion,
    Guid CategoriaId,
    string IconoId,
    IReadOnlyCollection<DayOfWeek> DiasProgramados);
