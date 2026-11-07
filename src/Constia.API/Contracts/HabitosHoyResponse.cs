namespace Constia.API.Contracts;

public sealed record HabitosHoyResponse(DateOnly Fecha, IReadOnlyList<HabitoDiarioResponse> Habitos);
