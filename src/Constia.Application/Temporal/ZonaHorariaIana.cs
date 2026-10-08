using NodaTime;

namespace Constia.Application.Temporal;

public sealed class ZonaHorariaIana : IZonaHorariaIana
{
    public const int LongitudMaximaIdentificador = 100;

    public bool EsValida(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)
            || timeZoneId.Length > LongitudMaximaIdentificador
            || DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZoneId) is null)
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    public TimeZoneInfo Resolver(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)
            || timeZoneId.Length > LongitudMaximaIdentificador
            || DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZoneId) is null)
        {
            throw new ZonaHorariaPersistidaInvalidaException(timeZoneId);
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new ZonaHorariaPersistidaInvalidaException(timeZoneId, exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new ZonaHorariaPersistidaInvalidaException(timeZoneId, exception);
        }
    }
}
