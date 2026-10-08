namespace Constia.Application.Temporal;

public sealed class ZonaHorariaIanaInvalidaException(string timeZoneId)
    : ArgumentException($"The time zone identifier '{timeZoneId}' is not a valid, supported IANA identifier.", nameof(timeZoneId));
