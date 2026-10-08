namespace Constia.Application.Temporal;

public interface IZonaHorariaIana
{
    bool EsValida(string? timeZoneId);

    TimeZoneInfo Resolver(string timeZoneId);
}
