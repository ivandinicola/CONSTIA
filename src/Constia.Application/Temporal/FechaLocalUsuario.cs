namespace Constia.Application.Temporal;

public sealed class FechaLocalUsuario(TimeProvider timeProvider, IZonaHorariaIana zonasHorarias)
    : IFechaLocalUsuario
{
    public DateOnly ObtenerHoy(string timeZoneId) =>
        ObtenerFechaLocal(timeProvider.GetUtcNow(), timeZoneId);

    public DateOnly ObtenerFechaLocal(DateTimeOffset instanteUtc, string timeZoneId)
    {
        var zona = zonasHorarias.Resolver(timeZoneId);
        var fechaHoraLocal = TimeZoneInfo.ConvertTime(instanteUtc, zona);
        return DateOnly.FromDateTime(fechaHoraLocal.DateTime);
    }
}
