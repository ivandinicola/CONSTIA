namespace Constia.Application.Temporal;

public sealed class FechaLocalUsuario(TimeProvider timeProvider, IZonaHorariaIana zonasHorarias)
    : IFechaLocalUsuario
{
    public DateOnly ObtenerHoy(string timeZoneId)
    {
        var zona = zonasHorarias.Resolver(timeZoneId);
        var instanteUtc = timeProvider.GetUtcNow();
        var fechaHoraLocal = TimeZoneInfo.ConvertTime(instanteUtc, zona);

        return DateOnly.FromDateTime(fechaHoraLocal.DateTime);
    }
}
