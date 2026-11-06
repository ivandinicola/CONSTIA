namespace Constia.API.Contracts;

/// <summary>Hábito programado para la fecha consultada y su cumplimiento registrado.</summary>
public sealed record HabitoDiarioResponse(
    Guid Id, string Nombre, string? Descripcion, Guid? CategoriaId, string? IconoId, bool Cumplido);
