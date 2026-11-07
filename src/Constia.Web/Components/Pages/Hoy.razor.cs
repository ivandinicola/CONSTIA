using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using Constia.Web.Configuration;
using Constia.Web.Habitos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;

namespace Constia.Web.Components.Pages;

public partial class Hoy
{
    [CascadingParameter] private HttpContext Http { get; set; } = default!;
    [Inject] private IHttpClientFactory Clients { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "cumplir")] private string? Solicitud { get; set; }
    private HabitosHoy? dia;
    private string? error;
    private string? mensaje;
    private Guid usuario;
    private bool enProceso;

    protected override async Task OnInitializedAsync()
    {
        Http.Response.Headers.CacheControl = "no-store";
        Guid.TryParse(Http.User.FindFirstValue(ClaimTypes.NameIdentifier), out usuario);
        enProceso = HttpMethods.IsPost(Http.Request.Method);
        if (enProceso)
            mensaje = "Registrando o comprobando el cumplimiento…";
        if (!HttpMethods.IsPost(Http.Request.Method))
        {
            using var client = await CrearClienteAsync();
            if (client is not null)
                Mostrar(await Cumplir.CargarAsync(client, Http.RequestAborted));
        }
    }

    private async Task RegistrarAsync()
    {
        using var client = await CrearClienteAsync();
        if (client is null)
            return;
        var partes = Solicitud?.Split('|');
        if (partes is not { Length: 2 } || !Guid.TryParse(partes[0], out var habito) ||
            !DateOnly.TryParseExact(partes[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            Mostrar((await Cumplir.CargarAsync(client, Http.RequestAborted)) with { Mensaje = "Revisá el hábito y la fecha antes de continuar." });
            return;
        }
        Mostrar(await Cumplir.RegistrarAsync(client, usuario, habito, fecha, Http.RequestAborted));
    }

    private async Task<HttpClient?> CrearClienteAsync()
    {
        var token = await Http.GetTokenAsync("access_token");
        if (usuario == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            Navigation.NavigateTo("/login", forceLoad: true);
            return null;
        }
        var client = Clients.CreateClient(ApiOptions.ClientName);
        if (client.BaseAddress is not { } api || (api.Scheme != Uri.UriSchemeHttps && !api.IsLoopback))
        {
            client.Dispose();
            error = "No pudimos conectar con la API.";
            return null;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private void Mostrar(ResultadoHoy resultado)
    {
        enProceso = false;
        dia = resultado.Dia;
        error = resultado.Error;
        mensaje = resultado.Mensaje;
        if (resultado.LoginRequerido)
            Navigation.NavigateTo("/login", forceLoad: true);
    }
}
