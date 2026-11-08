using System.Net.Http.Headers;
using System.Security.Claims;
using Constia.Web.Configuration;
using Constia.Web.Habitos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;

namespace Constia.Web.Components.Pages;

public partial class NuevoHabito
{
    [Parameter] public Guid? Id { get; set; }
    [CascadingParameter] private HttpContext Http { get; set; } = default!;
    [Inject] private IHttpClientFactory Clients { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "crear-habito")] private string? Accion { get; set; }
    [SupplyParameterFromForm(FormName = "crear-habito")] private int Revision { get; set; }
    [SupplyParameterFromForm(FormName = "crear-habito")] private string? Nombre { get; set; }
    [SupplyParameterFromForm(FormName = "crear-habito")] private Guid? CategoriaId { get; set; }
    [SupplyParameterFromForm(FormName = "crear-habito")] private string? IconoId { get; set; }
    [SupplyParameterFromForm(FormName = "crear-habito")] private int[]? Dias { get; set; }
    private EstadoAlta? b;
    private string? error;
    private string? aviso;
    private Guid usuario;
    private string? sesion;
    private DateTimeOffset vence;

    protected override async Task OnInitializedAsync()
    {
        Http.Response.Headers.CacheControl = "no-store";
        var auth = await Http.AuthenticateAsync();
        Guid.TryParse(Http.User.FindFirstValue(ClaimTypes.NameIdentifier), out usuario);
        sesion = auth.Properties?.GetString("constia-session-id");
        vence = auth.Properties?.ExpiresUtc ?? default;
        if (usuario == Guid.Empty || sesion is null || vence <= DateTimeOffset.UtcNow)
        { Navigation.NavigateTo("/login", forceLoad: true); return; }
        if (Id is null)
        {
            if (HttpMethods.IsPost(Http.Request.Method)) { error = "El borrador no está disponible. Volvé a Mis hábitos."; return; }
            using var client = await ClienteAsync();
            if (client is null) return;
            b = await Alta.IniciarAsync(client, usuario, sesion, vence, Http.RequestAborted);
            if (b is not null) Navigation.NavigateTo($"/habitos/nuevo/{b.Id}", forceLoad: true);
            else error = "Alcanzaste el límite de borradores de esta sesión. Volvé más tarde o terminá los que ya abriste.";
        }
        else
        {
            b = Alta.Leer(usuario, sesion, Id.Value);
            if (b is null) error = "Este borrador venció o no pertenece a tu sesión. No lo reconstruimos ni reenviamos. Revisá Mis hábitos antes de iniciar uno nuevo.";
            else if (b.LoginRequerido) Navigation.NavigateTo("/login", forceLoad: true);
        }
    }
    private async Task EjecutarAsync()
    {
        if (b is null || sesion is null || Id is null) return;
        using var client = await ClienteAsync();
        if (client is null) return;
        b = await Alta.AplicarAsync(client, usuario, sesion, Id.Value, Revision, Accion ?? "", new(Nombre, CategoriaId, IconoId, (Dias ?? []).Select(d => (DayOfWeek)d).ToArray()), Http.RequestAborted);
        if (b is null) error = "El borrador venció. No volvimos a enviarlo. Revisá Mis hábitos.";
        else if (b.LoginRequerido) Navigation.NavigateTo("/login", forceLoad: true);
        else aviso = b.Estado switch { "confirmado" => "Tu hábito se creó correctamente.", "incierto" => "El resultado del alta sigue incierto. No se reenviará.", "enviando" => "Hay un envío en curso.", _ => b.Error is null ? $"Paso {b.Paso} de 4." : "Revisá los datos indicados." };
    }
    private async Task<HttpClient?> ClienteAsync()
    {
        var token = await Http.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token)) { Navigation.NavigateTo("/login", forceLoad: true); return null; }
        var client = Clients.CreateClient(ApiOptions.ClientName);
        if (client.BaseAddress is not { } api || (api.Scheme != Uri.UriSchemeHttps && !api.IsLoopback))
        { client.Dispose(); error = "No pudimos conectar con la API."; return null; }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
