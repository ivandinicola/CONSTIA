using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Constia.Web.Habitos;

public sealed record DatosAlta(string? Nombre, Guid? CategoriaId, string? IconoId, DayOfWeek[] DiasProgramados);
public sealed record CategoriaAlta(Guid Id, string Nombre, int Orden);
public sealed record IconoAlta(string Id, string Nombre, int Orden);
public sealed record CatalogosAlta(CategoriaAlta[] Categorias, IconoAlta[] Iconos);
public sealed record EstadoAlta(Guid Id, int Revision, int Paso, DatosAlta Datos, CatalogosAlta? Catalogos,
    string Estado, HabitoGestion? Resultado = null, string? Error = null, string? Campo = null, bool LoginRequerido = false);

// shortcut: borradores y coordinación de una instancia; necesitan almacenamiento durable e idempotencia API antes de soportar réplicas o recuperación entre reinicios.
public sealed class AltaHabito(IMemoryCache cache, TimeProvider clock)
{
    private readonly object acceso = new();
    private sealed class Entrada(EstadoAlta estado, DateTimeOffset vence)
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly DateTimeOffset Vence = vence;
        public EstadoAlta Vista = estado;
    }
    private Dictionary<Guid, Entrada>? Buscar(Guid usuario, string sesion) =>
        cache.TryGetValue(("altas-web", usuario, sesion), out Dictionary<Guid, Entrada>? valor) ? valor : null;
    private Entrada? Encontrar(Guid usuario, string sesion, Guid id)
    {
        lock (acceso)
        {
            var borradores = Buscar(usuario, sesion);
            return borradores is not null && borradores.TryGetValue(id, out var e) && e.Vence > clock.GetUtcNow() ? e : null;
        }
    }
    public EstadoAlta? Leer(Guid usuario, string sesion, Guid id) =>
        Encontrar(usuario, sesion, id) is { } e ? Volatile.Read(ref e.Vista) : null;

    public async Task<EstadoAlta?> IniciarAsync(HttpClient client, Guid usuario, string sesion, DateTimeOffset vence, CancellationToken ct)
    {
        if (usuario == Guid.Empty || string.IsNullOrWhiteSpace(sesion) || vence <= clock.GetUtcNow()) return null;
        Entrada entrada;
        lock (acceso)
        {
            var borradores = Buscar(usuario, sesion);
            if (borradores is null)
            {
                borradores = new();
                cache.Set(("altas-web", usuario, sesion), borradores, vence);
            }
            foreach (var viejo in borradores.Where(b => b.Value.Vence <= clock.GetUtcNow()).Select(b => b.Key).ToArray()) borradores.Remove(viejo);
            if (borradores.Count >= 8) return null;
            var plazo = clock.GetUtcNow().AddHours(1);
            entrada = new(new(Guid.NewGuid(), 0, 1, new(null, null, null, []), null, "editable"), vence < plazo ? vence : plazo);
            borradores.Add(entrada.Vista.Id, entrada);
        }
        // No se publica una carga parcial de catálogos a una solicitud concurrente.
        await entrada.Gate.WaitAsync(ct);
        try { entrada.Vista = await CatalogosAsync(client, entrada.Vista, ct); return entrada.Vista; }
        finally { entrada.Gate.Release(); }
    }
    public async Task<EstadoAlta?> AplicarAsync(HttpClient client, Guid usuario, string sesion, Guid id, int revision, string accion, DatosAlta? datos, CancellationToken ct)
    {
        var entrada = Encontrar(usuario, sesion, id);
        if (entrada is null) return null;
        if (!await entrada.Gate.WaitAsync(0, ct)) return Volatile.Read(ref entrada.Vista);
        try
        {
            var b = entrada.Vista;
            if (b.Estado != "editable" || b.Revision != revision) return b;
            b = b with { Revision = b.Revision + 1, Error = null, Campo = null, LoginRequerido = false };
            if (datos is not null && accion is "volver" or "continuar")
            {
                // Solo el paso visible puede modificar su parte del borrador, también al volver.
                var d = b.Datos;
                b = b with { Datos = b.Paso switch {
                    1 => d with { Nombre = datos.Nombre, CategoriaId = datos.CategoriaId },
                    2 => d with { IconoId = datos.IconoId },
                    3 => d with { DiasProgramados = datos.DiasProgramados.ToArray() },
                    _ => d } };
            }
            if (accion == "catalogos") b = await CatalogosAsync(client, b, ct);
            else if (accion == "volver") b = b with { Paso = Math.Max(1, b.Paso - 1) };
            else if (accion == "continuar")
            {
                b = Validar(b, b.Paso);
                if (b.Error is null) b = b with { Paso = Math.Min(4, b.Paso + 1) };
            }
            else if (accion == "crear" && b.Paso == 4)
            {
                b = Validar(b, 3);
                if (b.Error is null)
                {
                    entrada.Vista = b with { Estado = "enviando" };
                    b = await EnviarAsync(client, b, ct);
                }
            }
            else b = b with { Error = "Revisá el paso actual antes de continuar." };
            Volatile.Write(ref entrada.Vista, b);
            return b;
        }
        finally { entrada.Gate.Release(); }
    }
    private static EstadoAlta Validar(EstadoAlta b, int hasta)
    {
        if (b.Catalogos is null) return b with { Error = "No pudimos cargar las opciones. Reintentá la carga antes de continuar." };
        if (string.IsNullOrWhiteSpace(b.Datos.Nombre)) return b with { Paso = 1, Campo = "nombre", Error = "Ingresá un nombre para tu hábito." };
        if (!b.Catalogos.Categorias.Any(c => c.Id == b.Datos.CategoriaId)) return b with { Paso = 1, Campo = "categoriaId", Error = "Elegí una categoría." };
        if (hasta >= 2 && !b.Catalogos.Iconos.Any(i => i.Id == b.Datos.IconoId)) return b with { Paso = 2, Campo = "iconoId", Error = "Elegí un ícono." };
        if (hasta >= 3 && (b.Datos.DiasProgramados.Length == 0 || b.Datos.DiasProgramados.Any(d => !Enum.IsDefined(d))))
            return b with { Paso = 3, Campo = "diasProgramados", Error = "Elegí al menos un día válido." };
        return b with { Datos = b.Datos with { DiasProgramados = PresentacionHabitos.Semana.Select(d => d.Dia).Where(b.Datos.DiasProgramados.Contains).ToArray() } };
    }
    private static async Task<EstadoAlta> CatalogosAsync(HttpClient client, EstadoAlta b, CancellationToken ct)
    {
        try
        {
            using var categorias = await client.GetAsync("api/habitos/categorias", ct);
            using var iconos = await client.GetAsync("api/habitos/iconos", ct);
            if (categorias.StatusCode == HttpStatusCode.Unauthorized || iconos.StatusCode == HttpStatusCode.Unauthorized)
                return b with { Catalogos = null, LoginRequerido = true, Error = "Tu sesión ya no es válida." };
            if (categorias.StatusCode == HttpStatusCode.OK && iconos.StatusCode == HttpStatusCode.OK)
            {
                var c = await categorias.Content.ReadFromJsonAsync<CategoriaAlta[]>(ct);
                var i = await iconos.Content.ReadFromJsonAsync<IconoAlta[]>(ct);
                if (c is { Length: 10 } && i is { Length: 16 } &&
                    c.All(x => x is not null && x.Id != Guid.Empty && !string.IsNullOrWhiteSpace(x.Nombre)) &&
                    i.All(x => x is not null && PresentacionHabitos.Icono(x.Id) is not null && !string.IsNullOrWhiteSpace(x.Nombre)) &&
                    c.Select(x => x.Id).Distinct().Count() == 10 && i.Select(x => x.Id).Distinct().Count() == 16 &&
                    c.Select(x => x.Orden).Order().SequenceEqual(Enumerable.Range(1,10)) && i.Select(x => x.Orden).Order().SequenceEqual(Enumerable.Range(1,16)))
                    return b with { Catalogos = new(c.OrderBy(x => x.Orden).ToArray(), i.OrderBy(x => x.Orden).ToArray()), Error = null };
            }
        }
        catch (Exception ex) when (RespuestaFallida(ex)) { /* No usar opciones inventadas ante catálogos incompletos. */ }
        return b with { Catalogos = null, Error = "No pudimos cargar las categorías y los íconos. Volvé a intentar." };
    }
    private static async Task<EstadoAlta> EnviarAsync(HttpClient client, EstadoAlta b, CancellationToken ct)
    {
        try
        {
            using var response = await client.PostAsJsonAsync("api/habitos", new {
                nombre = b.Datos.Nombre, categoriaId = b.Datos.CategoriaId, iconoId = b.Datos.IconoId, diasProgramados = b.Datos.DiasProgramados }, ct);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                var h = await response.Content.ReadFromJsonAsync<HabitoGestion>(ct);
                if (h is not null && h.Id != Guid.Empty && h.Nombre == b.Datos.Nombre && h.CategoriaId == b.Datos.CategoriaId &&
                    h.IconoId == b.Datos.IconoId && h.Estado == 0 && h.StartDate != default && h.CreatedAt != default &&
                    h.DiasProgramados is { Length: > 0 } && h.DiasProgramados.Length == b.Datos.DiasProgramados.Length &&
                    h.DiasProgramados.ToHashSet().SetEquals(b.Datos.DiasProgramados))
                    return b with { Estado = "confirmado", Resultado = h };
            }
            else if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var campo = "nombre";
                try
                {
                    using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    if (problem.RootElement.ValueKind == JsonValueKind.Object && problem.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                        campo = errors.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).FirstOrDefault(k => k is "nombre" or "categoriaid" or "iconoid" or "diasprogramados") ?? campo;
                }
                catch (JsonException) { /* El rechazo es conocido aunque falte un cuerpo ProblemDetails. */ }
                return b with { Paso = campo == "iconoid" ? 2 : campo == "diasprogramados" ? 3 : 1, Campo = campo,
                    Error = "La API rechazó los datos. Revisá el campo indicado y volvé a confirmar." };
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
                return b with { LoginRequerido = true, Error = "Tu sesión ya no es válida. Volvé a iniciar sesión." };
            else if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                return b with { Error = "El alta no está disponible: falta inicializar el historial o el servicio está temporalmente fuera de servicio. Intentá más tarde." };
        }
        catch (Exception ex) when (RespuestaFallida(ex)) { /* La API pudo guardar sin que llegara una respuesta válida. */ }
        return b with { Estado = "incierto", Error = "No pudimos confirmar si se creó el hábito. No volveremos a enviar este borrador. Revisá Mis hábitos; una coincidencia no demuestra el resultado de este envío." };
    }
    private static bool RespuestaFallida(Exception ex) => ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException;
}
