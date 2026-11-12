using System.Globalization;
using System.Net.Http.Headers;
using Constia.Web.Configuration;
using Constia.Web.Habitos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;

namespace Constia.Web.Components.Pages;

public partial class HistorialHabito
{
    [Parameter] public Guid Id { get; set; }
    [SupplyParameterFromQuery] public string? Mes { get; set; }
    [SupplyParameterFromQuery] public string? Origen { get; set; }
    [SupplyParameterFromQuery] public string? Foco { get; set; }
    [SupplyParameterFromQuery] public string? Fecha { get; set; }
    [CascadingParameter] private HttpContext Http { get; set; } = default!;
    [Inject] private IHttpClientFactory Clients { get; set; } = default!;

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-AR");
    private ResultadoHistorial? resultado;
    private string? nombreDuranteCarga;
    private bool Inactivos => Origen == "inactivos";
    private string VolverUrl => $"/habitos{(Inactivos ? "/inactivos" : "")}?historial={Id}";

    protected override async Task OnInitializedAsync()
    {
        Http.Response.Headers.CacheControl = "no-store";
        var token = await Http.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token))
        {
            Navigation.NavigateTo("/login", forceLoad: true);
            return;
        }

        using var client = Clients.CreateClient(ApiOptions.ClientName);
        if (client.BaseAddress is not { } api || (api.Scheme != Uri.UriSchemeHttps && !api.IsLoopback))
        {
            resultado = new(Error: "No pudimos conectar con la API.");
            return;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        resultado = await Consulta.CargarAsync(client, Id, Mes, Http.RequestAborted, nombre =>
        {
            nombreDuranteCarga = nombre;
            StateHasChanged();
        });
        if (resultado.Login) Navigation.NavigateTo("/login", forceLoad: true);
    }

    private string EstadisticasUrl(DateOnly? mes, DateOnly? fecha) =>
        ConsultarEstadisticasHabito.Url(Id, true, Inactivos,
            mes?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? Mes,
            mes is null ? Fecha : fecha?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    private DiaHistorial Seleccionar(MesHistorial vista)
    {
        if (DateOnly.TryParseExact(Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            var indice = fecha.DayNumber - vista.Mes.DayNumber;
            if (indice >= 0 && indice < vista.Dias.Length)
                return vista.Dias[indice];
        }

        return vista.Mes.Year == vista.Hoy.Year && vista.Mes.Month == vista.Hoy.Month ? vista.Dias[^1] : vista.Dias[0];
    }

    private string MesUrl(DateOnly mes, int delta) =>
        $"/habitos/{Id}/historial?mes={mes.AddMonths(delta):yyyy-MM}&origen={(Inactivos ? "inactivos" : "activos")}&foco={(delta < 0 ? "anterior" : "siguiente")}";

    private static (string Signo, string Texto) Estado(string estado) => estado switch
    {
        "cumplido" => ("✓", "Cumplido"),
        "pendiente" => ("○", "Pendiente"),
        "no_programado" => ("–", "No programado"),
        _ => ("?", "Desconocido")
    };

    private static string FechaTexto(DateOnly fecha) => fecha.ToString("d 'de' MMMM", Cultura);
    private static string Registro(DiaHistorial dia) => dia.TieneRegistro ? $"Guardado el {dia.Fecha:d/M/yyyy}" : "Sin registro";
}
