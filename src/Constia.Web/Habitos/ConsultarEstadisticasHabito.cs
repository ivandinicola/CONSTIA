using System.Globalization;

namespace Constia.Web.Habitos;

public static class ConsultarEstadisticasHabito
{
    public static async Task<(DetalleEstadisticasHabito? Habito, bool Login)> DetalleAsync(
        HttpClient client, Guid id, CancellationToken ct)
    {
        var (estado, detalle) = await ConsultarProgreso.LeerAsync<Detalle>(client, $"api/habitos/{id}", ct);
        return (detalle is { Estado: 0 or 1 } && !string.IsNullOrWhiteSpace(detalle.Nombre)
            ? new(detalle.Nombre, detalle.Estado == 1)
            : null, estado == EstadoProgreso.Login);
    }

    public static async Task<SeccionProgreso> RachasAsync(
        HttpClient client, Guid id, DetalleEstadisticasHabito habito, CancellationToken ct)
    {
        var resultado = await ConsultarProgreso.RachasAsync(client, $"api/habitos/{id}/rachas", ct);
        return resultado with
        {
            HabitoInactivo = resultado.Rachas is { } rachas
                ? rachas.Actual.Estado == "no_aplicable"
                : habito.Inactivo
        };
    }

    public static async Task<SeccionProgreso> PeriodoAsync(HttpClient client, Guid id,
        Task<(DateOnly? Hoy, bool Login)> local, CancellationToken ct)
    {
        var (hoy, login) = await local;
        if (login)
            return new(EstadoProgreso.Login);
        if (hoy is not { DayNumber: >= 29 } hasta)
            return new(EstadoProgreso.Error);

        var desde = hasta.AddDays(-29);
        var ruta = FormattableString.Invariant($"api/habitos/{id}/historial?desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}");
        var (estado, dias) = await ConsultarProgreso.LeerAsync<DiaHistorial[]>(client, ruta, ct);
        if (estado != EstadoProgreso.Disponible)
            return new(estado, Hoy: hasta);
        if (!ConsultarHistorialHabito.RangoValido(dias, desde, hasta))
            return new(EstadoProgreso.Error, Hoy: hasta);

        return new(estado, Hoy: hasta, PeriodoHabito: new(dias!));
    }

    internal static string Url(Guid id, bool estadisticas, bool inactivos, string? mes, string? fecha)
    {
        var ruta = $"/habitos/{id}/{(estadisticas ? "estadisticas" : "historial")}";
        var query = $"?origen={(inactivos ? "inactivos" : "activos")}";
        if (!estadisticas)
            query += "&foco=estadisticas";
        if (DateOnly.TryParseExact(mes + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            query += $"&mes={mes}";
        if (DateOnly.TryParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            query += $"&fecha={fecha}";
        return ruta + query;
    }

    private sealed record Detalle(string Nombre, int? Estado);
}

public sealed record DetalleEstadisticasHabito(string Nombre, bool Inactivo);

public sealed record PeriodoEstadisticasHabito(DiaHistorial[] Dias)
{
    public int Cumplidos { get; } = Dias.Count(d => d.Estado == "cumplido");
    public int Programados { get; } = Dias.Count(d => d.Estado is "cumplido" or "pendiente");
    public bool HistorialParcial { get; } = Dias.Any(d => d.Estado == "desconocido");

    public int? Porcentaje
    {
        get
        {
            if (Programados == 0)
                return null;

            // Un día desconocido solo cambiaría la proporción si pudiera sumar una oportunidad de un resultado distinto.
            if (HistorialParcial && Dias.Any(d => d.Estado == "desconocido" &&
                (d.TieneRegistro ? Cumplidos != Programados : Cumplidos != 0)))
                return null;

            return (int)Math.Round(100m * Cumplidos / Programados, MidpointRounding.AwayFromZero);
        }
    }
}
