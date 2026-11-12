using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Constia.Web.Habitos;

public sealed class ConsultarHistorialHabito
{
    public async Task<ResultadoHistorial> CargarAsync(HttpClient client, Guid id, string? mes, CancellationToken ct, Action<string>? nombreDisponible = null)
    {
        var local = await ConsultarCalendario.LeerAsync<FechaLocal>(client, "api/usuarios/mi-fecha-local", ct);
        if (local.Datos is not { } hoy || hoy.Fecha == default)
            return new(Error: local.Error ?? "No pudimos determinar tu fecha local.", Login: local.Login);

        var actual = new DateOnly(hoy.Fecha.Year, hoy.Fecha.Month, 1);
        var visible = actual;
        if (mes is not null &&
            (!DateOnly.TryParseExact(mes + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out visible)
                || visible > actual))
            return new(Error: "Elegí un mes válido hasta el actual.");

        var detalle = await ConsultarCalendario.LeerAsync<NombreHabito>(client, $"api/habitos/{id}", ct);
        if (detalle.Datos is not { } habito || string.IsNullOrWhiteSpace(habito.Nombre))
            return new(Error: detalle.Error ?? "No pudimos cargar el hábito.", Login: detalle.Login);

        nombreDisponible?.Invoke(habito.Nombre);

        var finMes = new DateOnly(visible.Year, visible.Month, DateTime.DaysInMonth(visible.Year, visible.Month));
        var hasta = hoy.Fecha < finMes ? hoy.Fecha : finMes;
        try
        {
            using var respuesta = await client.GetAsync(
                $"api/habitos/{id}/historial?desde={visible:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}", ct);
            if (respuesta.StatusCode == HttpStatusCode.Unauthorized) return new(Login: true);
            if (respuesta.StatusCode == HttpStatusCode.ServiceUnavailable && await HistorialSinInicializarAsync(respuesta, ct))
                return new(Nombre: habito.Nombre, Error: "El historial todavía no está inicializado.", SinInicializar: true);
            if (!respuesta.IsSuccessStatusCode)
                return new(Nombre: habito.Nombre, Error: "Volvé a intentar o regresá a tus hábitos.");

            var dias = await respuesta.Content.ReadFromJsonAsync<DiaHistorial[]>(ct);
            if (!RangoValido(dias, visible, hasta))
                return new(Nombre: habito.Nombre, Error: "La respuesta del historial no es válida. Volvé a intentar.");

            return new(habito.Nombre, new(hoy.Fecha, visible, dias!), HabitoInactivo: habito.Estado == 1);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            return new(Nombre: habito.Nombre, Error: "Revisá tu conexión e intentá nuevamente.");
        }
    }

    internal static bool RangoValido(DiaHistorial[]? dias, DateOnly desde, DateOnly hasta) =>
        dias is not null && dias.Length == hasta.DayNumber - desde.DayNumber + 1 &&
        !dias.Where((d, i) => d is null || d.Fecha.DayNumber != desde.DayNumber + i ||
            d.Estado is not ("cumplido" or "pendiente" or "no_programado" or "desconocido")).Any();

    internal static async Task<bool> HistorialSinInicializarAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var error = await respuesta.Content.ReadFromJsonAsync<ErrorHistorial>(ct);
            return error?.Code == "habit_status_history_not_initialized";
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private sealed record ErrorHistorial(string? Code);
    private sealed record FechaLocal(DateOnly Fecha);
    private sealed record NombreHabito(string Nombre, int? Estado);
}

public sealed record DiaHistorial(DateOnly Fecha, string Estado, bool TieneRegistro);
public sealed record MesHistorial(DateOnly Hoy, DateOnly Mes, DiaHistorial[] Dias);
public sealed record ResultadoHistorial(
    string? Nombre = null, MesHistorial? Vista = null, string? Error = null, bool Login = false, bool SinInicializar = false, bool HabitoInactivo = false);
