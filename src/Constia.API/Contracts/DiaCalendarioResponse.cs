namespace Constia.API.Contracts;

/// <summary>Conteos de oportunidades programadas y cumplidas para una fecha civil.</summary>
public sealed record DiaCalendarioResponse(DateOnly Fecha, int Programados, int Cumplidos);
