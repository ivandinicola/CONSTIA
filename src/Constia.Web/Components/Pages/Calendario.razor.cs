using System.Globalization;
using System.Net.Http.Headers;
using Constia.Web.Configuration;
using Constia.Web.Habitos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
namespace Constia.Web.Components.Pages;
public partial class Calendario
{
    [CascadingParameter] private HttpContext Http { get; set; } = default!;
    [Inject] private IHttpClientFactory Clients { get; set; } = default!;
    [SupplyParameterFromQuery(Name = "mes")] public string? Mes { get; set; }
    [SupplyParameterFromQuery(Name = "fecha")] public string? Fecha { get; set; }
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-AR");
    private VistaCalendario? vista;
    private string? error;
    private string? anuncio;
    protected override async Task OnInitializedAsync()
    {
        Http.Response.Headers.CacheControl = "no-store";
        var token = await Http.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token)) { Navigation.NavigateTo("/login", forceLoad: true); return; }
        using var client = Clients.CreateClient(ApiOptions.ClientName);
        if (client.BaseAddress is not { } api || (api.Scheme != Uri.UriSchemeHttps && !api.IsLoopback)) { error = "No pudimos conectar con la API."; return; }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resultado = await Consulta.CargarAsync(client, Mes, Fecha, Http.RequestAborted);
        if (resultado.LoginRequerido) { Navigation.NavigateTo("/login", forceLoad: true); return; }
        vista = resultado.Vista; error = resultado.Error;
        anuncio = vista is null ? "El calendario no está disponible." : $"{vista.Mes.ToString("MMMM yyyy", Cultura)}. Fecha seleccionada: {vista.Seleccion.ToString("D", Cultura)}.";
    }
    private string MesUrl(int delta) => $"/calendario?mes={vista!.Mes.AddMonths(delta):yyyy-MM}";
    private string FechaUrl(DateOnly f) => $"/calendario?mes={vista!.Mes:yyyy-MM}&fecha={f:yyyy-MM-dd}";
    private string EstadoDia(DateOnly f, DiaResumen? r) => r is null ? "" : r.Programados == 0 ? "neutral" : f > vista!.Hoy ? "future" : r.Cumplidos == r.Programados ? "complete" : r.Cumplidos > 0 ? "partial" : "none";
    private string Etiqueta(DateOnly f, DiaResumen? r) => $"{f.ToString("D", Cultura)}. " +
        (f == vista!.Hoy ? "Hoy. " : "") + (f == vista.Seleccion ? "Seleccionado. " : "") +
        (f > vista.Hoy ? "Planificación futura. " : "") +
        (r is null ? "Resumen del rango no disponible." : r.Programados == 0 ? "Sin hábitos programados." : f > vista.Hoy ? $"{r.Programados} hábitos programados." : $"{r.Cumplidos} de {r.Programados} realizados.");
}
