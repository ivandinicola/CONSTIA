using System.Net.Http.Headers;
using System.Security.Claims;
using Constia.Web.Configuration;
using Constia.Web.Habitos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Caching.Memory;

namespace Constia.Web.Components.Pages;

public partial class Habitos
{
    [CascadingParameter] private HttpContext Http { get; set; } = default!;
    [Inject] private IHttpClientFactory Clients { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "gestionar-habitos")] private string? Accion { get; set; }
    [Inject] private IMemoryCache Cache { get; set; } = default!;
    [SupplyParameterFromQuery(Name = "edicion")] private Guid? Edicion { get; set; }
    [SupplyParameterFromQuery(Name = "volver")] private string? Volver { get; set; }
    private bool inactivos;
    private HabitoGestion[]? habitos;
    private HabitoGestion? confirmar;
    private Guid? volverFoco;
    private string? error;
    private string? mensaje;
    private Guid usuario;
    private static readonly (DayOfWeek Dia, string Corto, string Nombre)[] Semana = PresentacionHabitos.Semana;
    private static string Resumen(DayOfWeek[] dias) => dias.Distinct().Count() == 7 ? "Todos los días" : PresentacionHabitos.Resumen(dias);
    private static string? Icono(string? id) => PresentacionHabitos.Icono(id);
    protected override async Task OnInitializedAsync()
    {
        Http.Response.Headers.CacheControl = "no-store";
        inactivos = string.Equals(Http.Request.Path.Value?.TrimEnd('/'), "/habitos/inactivos", StringComparison.OrdinalIgnoreCase);
        Guid.TryParse(Http.User.FindFirstValue(ClaimTypes.NameIdentifier), out usuario);
        if (!HttpMethods.IsPost(Http.Request.Method))
        {
            using var client = await ClienteAsync();
            if (client is not null)
                Mostrar(inactivos
                    ? await Gestion.ListarInactivosAsync(client, Http.RequestAborted)
                    : await Gestion.ListarAsync(client, Http.RequestAborted));
            if (inactivos) return;
            if (Edicion is Guid aviso)
            {
                var key = (Http.User.FindFirstValue(ClaimTypes.NameIdentifier), aviso);
                if (Cache.TryGetValue<Guid>(key, out var editado) && habitos?.Any(h => h.Id == editado) == true)
                {
                    Cache.Remove(key);
                    mensaje = "Cambios guardados.";
                    volverFoco = editado;
                }
            }
        }
    }
    private async Task EjecutarAsync()
    {
        if (inactivos) return;
        using var client = await ClienteAsync();
        if (client is null) return;
        var partes = Accion?.Split('|');
        if (partes is not { Length: 2 } || !Guid.TryParse(partes[1], out var id) || id == Guid.Empty ||
            partes[0] is not ("pedir" or "confirmar" or "comprobar" or "cancelar"))
        {
            Mostrar((await Gestion.ListarAsync(client, Http.RequestAborted)) with { Mensaje = "Revisá el hábito seleccionado." }); return;
        }
        if (partes[0] is "pedir" or "cancelar")
        {
            Mostrar(await Gestion.ListarAsync(client, Http.RequestAborted));
            if (partes[0] == "cancelar") { volverFoco = id; mensaje = "Desactivación cancelada. No enviamos la operación."; return; }
            confirmar = habitos?.SingleOrDefault(h => h.Id == id);
            if (confirmar is null && error is null) mensaje = "La acción ya no está disponible. Revisá la lista actualizada.";
            return;
        }
        Mostrar(await Gestion.DesactivarAsync(client, usuario, id, Http.RequestAborted, soloComprobar: partes[0] == "comprobar"));
    }
    private async Task<HttpClient?> ClienteAsync()
    {
        var token = await Http.GetTokenAsync("access_token");
        if (usuario == Guid.Empty || string.IsNullOrWhiteSpace(token)) { Navigation.NavigateTo("/login", forceLoad: true); return null; }
        var client = Clients.CreateClient(ApiOptions.ClientName);
        if (client.BaseAddress is not { } api || (api.Scheme != Uri.UriSchemeHttps && !api.IsLoopback))
        { client.Dispose(); error = "No pudimos conectar con la API."; return null; }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
    private void Mostrar(ResultadoGestion r)
    {
        habitos = r.Habitos; error = r.Error; mensaje = r.Mensaje;
        if (r.LoginRequerido) Navigation.NavigateTo("/login", forceLoad: true);
    }
}
