using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Constia.Web.Configuration;
using Constia.Web.Habitos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Caching.Memory;

namespace Constia.Web.Components.Pages;

public partial class EditarHabito
{
    [Parameter] public Guid Id { get; set; }
    [CascadingParameter] private HttpContext Http { get; set; } = default!;
    [Inject] private IHttpClientFactory Clients { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IMemoryCache Cache { get; set; } = default!;
    [SupplyParameterFromForm(FormName = "editar-habito")] private Borrador? Datos { get; set; }
    [SupplyParameterFromForm(FormName = "editar-habito")] private string? Accion { get; set; }

    private HabitoGestion? actual;
    private CategoriaAlta[] categorias = [];
    private IconoAlta[] iconos = [];
    private string? error;
    private bool comparar;
    private bool confirmar;
    private bool revisar;
    private DateOnly? inicioFuturo;

    protected override async Task OnInitializedAsync()
    {
        Http.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsPost(Http.Request.Method))
            await CargarAsync();
    }

    private async Task CargarAsync()
    {
        if (Datos is { } borrador) borrador.Dias ??= [];

        var token = await Http.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token))
        {
            Navigation.NavigateTo("/login", forceLoad: true);
            return;
        }

        using var client = Clients.CreateClient(ApiOptions.ClientName);
        if (client.BaseAddress is not { } api || (api.Scheme != Uri.UriSchemeHttps && !api.IsLoopback))
        {
            error = "No pudimos conectar con la API.";
            return;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var ct = Http.RequestAborted;
        var ruta = $"api/habitos/{Id:D}";
        var guardando = false;
        try
        {
            using var detalle = await client.GetAsync(ruta, ct);
            if (!PuedeContinuar(detalle)) return;
            actual = await detalle.Content.ReadFromJsonAsync<HabitoGestion>(ct);
            var version = detalle.Headers.ETag;
            if (actual is null || actual.Id != Id || actual.Estado != 0 ||
                version is null || version.IsWeak || version.Tag == "*")
            {
                error = "El hábito activo o su versión no están disponibles para editar.";
                actual = null;
                return;
            }

            using var respuestaCategorias = await client.GetAsync("api/habitos/categorias", ct);
            using var respuestaIconos = await client.GetAsync("api/habitos/iconos", ct);
            if (!PuedeContinuar(respuestaCategorias) || !PuedeContinuar(respuestaIconos)) return;
            categorias = await respuestaCategorias.Content.ReadFromJsonAsync<CategoriaAlta[]>(ct) ?? [];
            iconos = PresentacionHabitos.OrdenarCatalogoIconos(await respuestaIconos.Content.ReadFromJsonAsync<IconoAlta[]>(ct)) ?? [];
            if (categorias.Length == 0 || iconos.Length == 0)
            {
                error = "No pudimos cargar las opciones. Reintentá sin perder el borrador.";
                return;
            }

            if (Datos is null)
            {
                Datos = new Borrador
                {
                    Nombre = actual.Nombre, Descripcion = actual.Descripcion, Dias = actual.DiasProgramados,
                    CategoriaId = actual.CategoriaId, IconoId = actual.IconoId, Version = version.ToString()
                };
                return;
            }

            if (Accion == "revisar")
            {
                Datos.Version = version.ToString();
                comparar = true;
                return;
            }
            if (Datos.Version != version.ToString())
            {
                revisar = true;
                error = "El hábito cambió desde que abriste el formulario.";
                return;
            }
            if (Accion is not ("guardar" or "confirmar")) return;
            if (string.IsNullOrWhiteSpace(Datos.Nombre) || Datos.Dias.Length == 0 ||
                Datos.Dias.Any(d => !Enum.IsDefined(d)))
            {
                error = "Ingresá un nombre y elegí al menos un día válido.";
                return;
            }

            if (!actual.DiasProgramados.ToHashSet().SetEquals(Datos.Dias) && Accion != "confirmar")
            {
                using var fecha = await client.GetAsync("api/usuarios/mi-fecha-local", ct);
                if (!PuedeContinuar(fecha)) return;
                var local = await fecha.Content.ReadFromJsonAsync<FechaLocal>(ct);
                if (local is null || local.Fecha == default)
                {
                    error = "No pudimos determinar tu fecha local.";
                    return;
                }
                confirmar = true;
                inicioFuturo = local.Fecha < actual.StartDate ? actual.StartDate : null;
                return;
            }

            var iconoElegido = Datos.IconoId;
            var cambiarIcono = !string.IsNullOrEmpty(iconoElegido) && iconoElegido != actual.IconoId;
            if (cambiarIcono && !iconos.Any(icono => icono.Id == iconoElegido))
            {
                error = "Ese ícono ya no está disponible. Elegí otro o conservá el ícono actual.";
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Put, ruta);
            request.Headers.IfMatch.Add(version);
            var cuerpo = new Dictionary<string, object?>
            {
                ["nombre"] = Datos.Nombre, ["descripcion"] = Datos.Descripcion, ["diasProgramados"] = Datos.Dias
            };
            if (Datos.CategoriaId is Guid categoria) cuerpo["categoriaId"] = categoria;
            if (cambiarIcono) cuerpo["iconoId"] = iconoElegido;
            request.Content = JsonContent.Create(cuerpo);
            guardando = true;
            using var respuesta = await client.SendAsync(request, ct);
            if (respuesta.StatusCode == HttpStatusCode.OK &&
                await respuesta.Content.ReadFromJsonAsync<HabitoGestion>(ct) is { } guardado && guardado.Id == Id)
            {
                var aviso = Guid.NewGuid();
                Cache.Set((Http.User.FindFirstValue(ClaimTypes.NameIdentifier), aviso), Id, TimeSpan.FromMinutes(2));
                Navigation.NavigateTo($"/habitos?edicion={aviso:D}");
                return;
            }
            if (respuesta.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                revisar = true;
                error = "El hábito cambió. Revisá la versión actual antes de guardar.";
                return;
            }
            if (respuesta.StatusCode == HttpStatusCode.Conflict)
            {
                error = "La fecha efectiva entra en conflicto con la programación existente.";
                return;
            }
            if (respuesta.StatusCode == HttpStatusCode.BadRequest)
            {
                error = "Revisá el nombre, la categoría, el ícono y los días elegidos.";
                return;
            }
            if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound)
            {
                PuedeContinuar(respuesta);
                return;
            }
            ResultadoIncierto();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            if (guardando) ResultadoIncierto();
            else error = "No pudimos conectar con la API. Tu borrador sigue disponible.";
        }
    }

    private bool PuedeContinuar(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.OK) return true;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            Navigation.NavigateTo("/login", forceLoad: true);
        error = response.StatusCode == HttpStatusCode.NotFound
            ? "El hábito ya no está disponible."
            : "No pudimos cargar la información. Tu borrador sigue disponible.";
        return false;
    }

    private void ResultadoIncierto()
    {
        revisar = true;
        error = "Todavía no sabemos si se guardó. Tu borrador sigue disponible. Compará la versión actual antes de volver a guardar.";
    }

    private sealed record FechaLocal(DateOnly Fecha);

    private sealed class Borrador
    {
        public string? Nombre { get; set; }
        public string? Descripcion { get; set; }
        public Guid? CategoriaId { get; set; }
        public string? IconoId { get; set; }
        public DayOfWeek[] Dias { get; set; } = [];
        public string? Version { get; set; }
    }
}
