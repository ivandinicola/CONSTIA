using System.Globalization;
using System.Net;
using System.Text.Json;
namespace Constia.Web.Habitos;

public sealed record DiaResumen(DateOnly Fecha, int Programados, int Cumplidos);
public sealed record HabitoCalendario(Guid Id, string Nombre, string? Descripcion, bool Cumplido);
public sealed record VistaCalendario(DateOnly Hoy, DateOnly Mes, DateOnly Desde, DateOnly Hasta, DateOnly Seleccion,
    DiaResumen[]? Dias, HabitoCalendario[]? Habitos, string? ErrorRango, string? ErrorDetalle);
public sealed record ResultadoCalendario(VistaCalendario? Vista, string? Error = null, bool LoginRequerido = false);

public sealed class ConsultarCalendario
{
    public static (DateOnly Desde, DateOnly Hasta) Rango(DateOnly mes)
    {
        var primero = new DateOnly(mes.Year, mes.Month, 1);
        var ultimo = new DateOnly(mes.Year, mes.Month, DateTime.DaysInMonth(mes.Year, mes.Month));
        return (DateOnly.FromDayNumber(Math.Max(0, primero.DayNumber - ((int)primero.DayOfWeek + 6) % 7)),
            DateOnly.FromDayNumber(Math.Min(DateOnly.MaxValue.DayNumber, ultimo.DayNumber + (7 - (int)ultimo.DayOfWeek) % 7)));
    }
    public async Task<ResultadoCalendario> CargarAsync(HttpClient client, string? mes, string? fecha, CancellationToken ct)
    {
        var local = await LeerAsync<FechaLocal>(client, "api/usuarios/mi-fecha-local", ct);
        if (local.Datos is not { } l || l.Fecha == default)
            return new(null, local.Error ?? "No pudimos determinar tu fecha local.", local.Login);
        DateOnly mesVisible;
        if (mes is null) mesVisible = new(l.Fecha.Year, l.Fecha.Month, 1);
        else if (mes.Length != 7 || !DateOnly.TryParseExact(mes + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out mesVisible))
            return new(null, "El mes debe tener formato yyyy-MM.");
        var (desde, hasta) = Rango(mesVisible);
        var seleccion = l.Fecha.Year == mesVisible.Year && l.Fecha.Month == mesVisible.Month ? l.Fecha : mesVisible;
        if (fecha is not null && (!DateOnly.TryParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out seleccion) || seleccion < desde || seleccion > hasta))
            return new(null, "Elegí una fecha válida dentro del calendario visible.");
        var rango = await LeerAsync<DiaResumen[]>(client, $"api/habitos/calendario?desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}", ct);
        if (rango.Login) return new(null, rango.Error, true);
        var detalle = await LeerAsync<HabitoCalendario[]>(client, $"api/habitos/diarios?fecha={seleccion:yyyy-MM-dd}", ct);
        if (detalle.Login) return new(null, detalle.Error, true);
        var dias = rango.Datos;
        if (dias is not null && (dias.Length != hasta.DayNumber - desde.DayNumber + 1 ||
            dias.Where((d,i) => d is null || d.Fecha.DayNumber != desde.DayNumber + i || d.Programados < 0 || d.Cumplidos < 0 || d.Cumplidos > d.Programados).Any()))
            rango = (null, "La respuesta del rango no es válida. Volvé a intentar.", false);
        if (detalle.Datos is { } h && (h.Any(x => x is null || x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Nombre)) || h.Select(x => x.Id).Distinct().Count() != h.Length))
            detalle = (null, "La respuesta del detalle no es válida. Volvé a intentar.", false);
        return new(new(l.Fecha, mesVisible, desde, hasta, seleccion, rango.Datos, detalle.Datos, rango.Error, detalle.Error));
    }
    private sealed record FechaLocal(DateOnly Fecha);
    private static async Task<(T? Datos, string? Error, bool Login)> LeerAsync<T>(HttpClient client, string path, CancellationToken ct) where T : class
    {
        try
        {
            using var r = await client.GetAsync(path, ct);
            if (r.StatusCode == HttpStatusCode.OK)
            {
                var datos = await r.Content.ReadFromJsonAsync<T>(ct);
                return datos is null ? (null, "La respuesta no es válida. Volvé a intentar.", false) : (datos, null, false);
            }
            return (null, r.StatusCode switch {
                HttpStatusCode.Unauthorized => "Tu sesión ya no es válida.",
                HttpStatusCode.UnprocessableEntity => "No se conoce el estado histórico necesario para esta consulta.",
                HttpStatusCode.ServiceUnavailable => "El historial todavía no está inicializado o el servicio no está disponible.",
                HttpStatusCode.BadRequest => "El rango o la fecha solicitados no son válidos.",
                _ => "No pudimos cargar esta información. Volvé a intentar." }, r.StatusCode == HttpStatusCode.Unauthorized);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        { return (null, "No pudimos conectar con el servicio. Volvé a intentar.", false); }
    }
}
