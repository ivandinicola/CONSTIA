using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Constia.Web.Configuration;
using Microsoft.AspNetCore.Authentication;

namespace Constia.Web.Habitos;

public sealed class ConsultarProgreso(IHttpClientFactory clients)
{
    public async Task<HttpClient?> ClienteAsync(HttpContext http)
    {
        var token = await http.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var client = clients.CreateClient(ApiOptions.ClientName);
        if (client.BaseAddress is not { } api || (api.Scheme != Uri.UriSchemeHttps && !api.IsLoopback))
        {
            client.Dispose();
            throw new InvalidOperationException("La API necesita HTTPS fuera del host local.");
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<(DateOnly? Hoy, bool Login)> FechaLocalAsync(HttpClient client, CancellationToken ct)
    {
        var respuesta = await ConsultarCalendario.LeerAsync<FechaLocal>(client, "api/usuarios/mi-fecha-local", ct);
        return (respuesta.Datos is { Fecha.DayNumber: >= 29 } local ? local.Fecha : null, respuesta.Login);
    }

    public static async Task<SeccionProgreso> RachasAsync(HttpClient client, CancellationToken ct)
    {
        var (estado, datos) = await LeerAsync<RachasProgreso>(client, "api/habitos/rachas", ct);
        if (estado != EstadoProgreso.Disponible)
            return new(estado);

        return datos is { FechaCorte.DayNumber: > 0 } && Valida(datos.Actual) && Valida(datos.Mejor)
            ? new(estado, Rachas: datos)
            : new(EstadoProgreso.Error);
    }

    public static async Task<SeccionProgreso> PeriodoAsync(HttpClient client, Task<(DateOnly? Hoy, bool Login)> local, CancellationToken ct)
    {
        var (hoy, login) = await local;
        if (login)
            return new(EstadoProgreso.Login);
        if (hoy is not { DayNumber: >= 29 } hasta)
            return new(EstadoProgreso.Error);

        var desde = hasta.AddDays(-29);
        var ruta = FormattableString.Invariant($"api/habitos/calendario?desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}");
        var (estado, dias) = await LeerAsync<DiaResumen[]>(client, ruta, ct);
        if (estado != EstadoProgreso.Disponible)
            return new(estado, Hoy: hasta);

        if (dias is not { Length: 30 } || dias.Where((dia, i) => dia is null ||
            dia.Fecha.DayNumber != desde.DayNumber + i || dia.Programados < 0 ||
            dia.Cumplidos < 0 || dia.Cumplidos > dia.Programados).Any())
            return new(EstadoProgreso.Error, Hoy: hasta);

        return new(estado, Hoy: hasta, Periodo: new(dias));
    }

    private static bool Valida(MetricaProgreso? metrica) => metrica switch
    {
        { Estado: "disponible", Valor: >= 0 } => true,
        { Estado: "no_disponible" or "no_aplicable", Valor: null } => true,
        _ => false
    };

    private static async Task<(EstadoProgreso Estado, T? Datos)> LeerAsync<T>(
        HttpClient client, string ruta, CancellationToken ct) where T : class
    {
        try
        {
            using var respuesta = await client.GetAsync(ruta, ct);
            switch (respuesta.StatusCode)
            {
                case HttpStatusCode.OK:
                    return (EstadoProgreso.Disponible, await respuesta.Content.ReadFromJsonAsync<T>(ct));
                case HttpStatusCode.Unauthorized:
                    return (EstadoProgreso.Login, null);
                case HttpStatusCode.UnprocessableEntity:
                    return (EstadoProgreso.Insuficiente, null);
                case HttpStatusCode.ServiceUnavailable when await ConsultarHistorialHabito.HistorialSinInicializarAsync(respuesta, ct):
                    return (EstadoProgreso.SinInicializar, null);
                default:
                    return (EstadoProgreso.Error, null);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            return (EstadoProgreso.Error, null);
        }
    }

    public static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-AR");

    private sealed record FechaLocal(DateOnly Fecha);
}

public enum EstadoProgreso { Disponible, Insuficiente, SinInicializar, Error, Login }

public sealed record MetricaProgreso(string Estado, int? Valor);
public sealed record RachasProgreso(DateOnly FechaCorte, MetricaProgreso Actual, MetricaProgreso Mejor);
public sealed record SeccionProgreso(EstadoProgreso Estado, DateOnly? Hoy = null,
    RachasProgreso? Rachas = null, PeriodoProgreso? Periodo = null);

public sealed record PeriodoProgreso(DiaResumen[] Dias)
{
    public long Programados { get; } = Dias.Sum(dia => (long)dia.Programados);
    public long Cumplidos { get; } = Dias.Sum(dia => (long)dia.Cumplidos);
    public int? Porcentaje => Programados == 0 ? null : (int)Math.Round(100m * Cumplidos / Programados, MidpointRounding.AwayFromZero);
}
