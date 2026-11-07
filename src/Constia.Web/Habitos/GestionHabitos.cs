using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Constia.Web.Habitos;

public sealed record HabitoGestion(Guid Id, string Nombre, string? Descripcion, DateTimeOffset CreatedAt,
    DateOnly StartDate, int Estado, DayOfWeek[] DiasProgramados, Guid? CategoriaId, string? IconoId);
public sealed record ResultadoGestion(HabitoGestion[]? Habitos, string? Mensaje = null, string? Error = null, bool LoginRequerido = false);
public sealed record DesactivacionIncierta(Guid Id, string Nombre);

public sealed class GestionHabitos(IMemoryCache cache)
{
    private readonly object acceso = new();
    private sealed class Envio(string nombre)
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly string Nombre = nombre;
        public bool Incierto;
        public bool Confirmado;
    }
    private ConcurrentDictionary<Guid, Envio>? Buscar(Guid usuario) =>
        cache.TryGetValue(("desactivaciones-web", usuario), out ConcurrentDictionary<Guid, Envio>? valor) ? valor : null;
    public DesactivacionIncierta[] Inciertos(Guid usuario) => Buscar(usuario)?
        .Where(e => Volatile.Read(ref e.Value.Incierto)).Select(e => new DesactivacionIncierta(e.Key, e.Value.Nombre)).ToArray() ?? [];

    public async Task<ResultadoGestion> ListarAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync("api/habitos", ct);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var habitos = await response.Content.ReadFromJsonAsync<HabitoGestion[]>(ct);
                if (habitos is not null && habitos.All(h => Valido(h) && h.Estado == 0) &&
                    habitos.Select(h => h.Id).Distinct().Count() == habitos.Length)
                    return new(habitos);
            }
            return new(null, Error: Error(response.StatusCode), LoginRequerido: response.StatusCode == HttpStatusCode.Unauthorized);
        }
        catch (Exception ex) when (ErrorRespuesta(ex)) { return new(null, Error: "No pudimos cargar tus hábitos. Volvé a intentar."); }
    }
    public async Task<ResultadoGestion> DesactivarAsync(HttpClient client, Guid usuario, Guid id, CancellationToken ct, bool soloComprobar = false)
    {
        if (usuario == Guid.Empty || id == Guid.Empty) return new(null, Error: "Revisá el hábito seleccionado.");
        var envios = Buscar(usuario);
        Envio? envio = null;
        envios?.TryGetValue(id, out envio);
        if (envio is null)
        {
            var lista = await ListarAsync(client, ct);
            var habito = lista.Habitos?.SingleOrDefault(h => h.Id == id);
            if (lista.Habitos is null) return lista;
            if (habito is null || soloComprobar) return lista with { Mensaje = "La acción ya no está disponible. Revisá la lista actualizada." };
            lock (acceso)
            {
                // shortcut: una instancia, como las sesiones; compartir coordinación antes de desplegar réplicas.
                envios = cache.GetOrCreate(("desactivaciones-web", usuario), entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(3);
                    return new ConcurrentDictionary<Guid, Envio>();
                })!;
                envio = envios.GetOrAdd(id, _ => new Envio(habito.Nombre));
            }
        }
        if (!await envio.Gate.WaitAsync(0, ct))
            return (await ListarAsync(client, ct)) with { Mensaje = "Hay una desactivación en curso. Comprobá su estado antes de continuar." };
        try
        {
            if (envio.Confirmado)
                return (await ListarAsync(client, ct)) with { Mensaje = $"«{envio.Nombre}» está inactivo." };
            if (envio.Incierto || soloComprobar) return await ComprobarAsync(client, envio, id, ct);
            envio.Incierto = true;
            try
            {
                using var response = await client.DeleteAsync($"api/habitos/{id}", ct);
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    envio.Confirmado = true; envio.Incierto = false;
                    return (await ListarAsync(client, ct)) with { Mensaje = $"Desactivación confirmada de «{envio.Nombre}». Se conservan sus cumplimientos anteriores." };
                }
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadRequest)
                {
                    envio.Incierto = false;
                    var recarga = await ListarAsync(client, ct);
                    return recarga with { Mensaje = Error(response.StatusCode), LoginRequerido = recarga.LoginRequerido || response.StatusCode == HttpStatusCode.Unauthorized };
                }
            }
            catch (Exception ex) when (ErrorRespuesta(ex)) { /* La API pudo guardar aunque no llegara la respuesta. */ }
            return await ComprobarAsync(client, envio, id, ct);
        }
        finally { envio.Gate.Release(); }
    }
    private async Task<ResultadoGestion> ComprobarAsync(HttpClient client, Envio envio, Guid id, CancellationToken ct)
    {
        var mensaje = $"El resultado de «{envio.Nombre}» sigue incierto. Solo comprobaremos su estado; no volveremos a enviarlo.";
        try
        {
            using var response = await client.GetAsync($"api/habitos/{id}", ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return new(null, Error: Error(response.StatusCode), LoginRequerido: true);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var habito = await response.Content.ReadFromJsonAsync<HabitoGestion>(ct);
                if (Valido(habito) && habito!.Id == id && habito.Estado == 1)
                {
                    envio.Incierto = false; envio.Confirmado = true;
                    mensaje = $"«{envio.Nombre}» está inactivo. Comprobamos el estado final; no quién lo cambió.";
                }
            }
        }
        catch (Exception ex) when (ErrorRespuesta(ex)) { /* Conservar incertidumbre. */ }
        return (await ListarAsync(client, ct)) with { Mensaje = mensaje };
    }
    private static bool Valido(HabitoGestion? h) => h is not null && h.Id != Guid.Empty &&
        !string.IsNullOrWhiteSpace(h.Nombre) && h.DiasProgramados is { Length: > 0 } &&
        h.DiasProgramados.All(Enum.IsDefined) && h.Estado is 0 or 1;
    private static bool ErrorRespuesta(Exception e) => e is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException;
    private static string Error(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Tu sesión ya no es válida. Volvé a iniciar sesión.",
        HttpStatusCode.NotFound => "El hábito ya no está disponible para esta cuenta.",
        HttpStatusCode.Conflict => "El hábito cambió o tiene un conflicto de vigencia. Revisá su estado antes de volver a confirmar.",
        HttpStatusCode.ServiceUnavailable => "La operación no está disponible por el momento. Volvé a intentar más tarde.",
        _ => "No pudimos completar la operación. Volvé a intentar."
    };
}
