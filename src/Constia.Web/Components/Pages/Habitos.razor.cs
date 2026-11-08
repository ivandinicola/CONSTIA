using System.Net.Http.Headers;
using System.Security.Claims;
using Constia.Web.Configuration;
using Constia.Web.Habitos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;

namespace Constia.Web.Components.Pages;

public partial class Habitos
{
    [CascadingParameter] private HttpContext Http { get; set; } = default!;
    [Inject] private IHttpClientFactory Clients { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "gestionar-habitos")] private string? Accion { get; set; }
    private HabitoGestion[]? habitos;
    private HabitoGestion? confirmar;
    private Guid? volverFoco;
    private string? error;
    private string? mensaje;
    private Guid usuario;
    private static readonly (DayOfWeek Dia, string Corto, string Nombre)[] Semana =
    [ (DayOfWeek.Monday,"Lun","lunes"), (DayOfWeek.Tuesday,"Mar","martes"), (DayOfWeek.Wednesday,"Mié","miércoles"),
      (DayOfWeek.Thursday,"Jue","jueves"), (DayOfWeek.Friday,"Vie","viernes"), (DayOfWeek.Saturday,"Sáb","sábado"), (DayOfWeek.Sunday,"Dom","domingo") ];
    private static string Resumen(DayOfWeek[] dias) => dias.Distinct().Count() == 7 ? "Todos los días" :
        string.Join(" · ", Semana.Where(d => dias.Contains(d.Dia)).Select(d => d.Corto));
    private static string? Icono(string? id) => id switch
    {
        "libro" => "constia-lectura.png", "fuerza" => "constia-movimiento.png", "agua" => "constia-agua.png",
        "loto" => "constia-meditacion.png", "manzana" => "constia-alimentacion.png", "musica" => "constia-nota-musical.png",
        "luna" or "cafe" or "corredor" or "corazon" or "cerebro" or "sol" or "huella" or "brote" or "portatil" or "billete" => $"constia-{id}.png",
        _ => null
    };
    protected override async Task OnInitializedAsync()
    {
        Http.Response.Headers.CacheControl = "no-store";
        Guid.TryParse(Http.User.FindFirstValue(ClaimTypes.NameIdentifier), out usuario);
        if (!HttpMethods.IsPost(Http.Request.Method))
        {
            using var client = await ClienteAsync();
            if (client is not null) Mostrar(await Gestion.ListarAsync(client, Http.RequestAborted));
        }
    }
    private async Task EjecutarAsync()
    {
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
