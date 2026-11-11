namespace Constia.API.Contracts;

/// <summary>Rachas calculadas hasta la fecha civil local del usuario.</summary>
public sealed record RachasResponse(DateOnly FechaCorte, MetricaRachaResponse Actual, MetricaRachaResponse Mejor);

/// <summary>Estado de la métrica y su valor cuando está disponible.</summary>
public sealed record MetricaRachaResponse(string Estado, int? Valor);
