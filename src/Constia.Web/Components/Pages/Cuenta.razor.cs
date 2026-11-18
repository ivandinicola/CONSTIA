using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Constia.Web.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;

namespace Constia.Web.Components.Pages;

public partial class Cuenta
{
    private const string ZonaHorariaPath = "api/usuarios/mi-zona-horaria";

    [CascadingParameter] private HttpContext Http { get; set; } = default!;
    [Inject] private IHttpClientFactory Clients { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "cambiar-zona")] private string? Seleccion { get; set; }

    private string? zonaGuardada;
    private string? error;
    private bool zonaInvalida;
    private bool guardado;

    protected override async Task OnInitializedAsync()
    {
        Http.Response.Headers.CacheControl = "no-store";
        using var client = await ClienteAsync();
        if (client is null)
            return;

        try
        {
            using var response = await client.GetAsync(ZonaHorariaPath, Http.RequestAborted);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Navigation.NavigateTo("/login", forceLoad: true);
                return;
            }

            if (response.IsSuccessStatusCode)
                zonaGuardada = (await response.Content.ReadFromJsonAsync<ZonaHoraria>(Http.RequestAborted))?.TimeZoneId;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException ||
                                  ex is OperationCanceledException && !Http.RequestAborted.IsCancellationRequested)
        {
            // El usuario puede volver a cargar la zona guardada.
        }

        if (string.IsNullOrWhiteSpace(zonaGuardada))
        {
            zonaGuardada = null;
            error = "No pudimos consultar tu zona horaria. Volvé a intentar.";
        }
    }

    private async Task GuardarAsync()
    {
        if (string.IsNullOrWhiteSpace(Seleccion))
        {
            zonaInvalida = true;
            error = "Elegí una zona horaria IANA válida.";
            return;
        }

        using var client = await ClienteAsync();
        if (client is null)
            return;

        try
        {
            using var response = await client.PutAsJsonAsync(ZonaHorariaPath,
                new { timeZoneId = Seleccion }, Http.RequestAborted);
            switch (response.StatusCode)
            {
                case HttpStatusCode.NoContent:
                    zonaGuardada = Seleccion;
                    error = null;
                    guardado = true;
                    return;
                case HttpStatusCode.Unauthorized:
                    Navigation.NavigateTo("/login", forceLoad: true);
                    return;
                case HttpStatusCode.BadRequest:
                    zonaInvalida = true;
                    error = "Elegí una zona horaria IANA válida.";
                    return;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException ||
                                  ex is OperationCanceledException && !Http.RequestAborted.IsCancellationRequested)
        {
            // La selección viaja con la respuesta para permitir un nuevo intento.
        }

        error = "No pudimos confirmar el cambio de zona horaria. Volvé a intentar.";
    }

    private async Task<HttpClient?> ClienteAsync()
    {
        var token = await Http.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token))
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

    private sealed record ZonaHoraria(string TimeZoneId);
}
