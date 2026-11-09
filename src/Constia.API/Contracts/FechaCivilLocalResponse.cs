namespace Constia.API.Contracts;

/// <summary>Fecha civil actual calculada con la zona IANA persistida del usuario autenticado.</summary>
public sealed record FechaCivilLocalResponse(DateOnly Fecha);
