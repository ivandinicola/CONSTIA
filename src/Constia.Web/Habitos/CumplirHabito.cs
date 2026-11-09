using System.Net;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Constia.Web.Habitos;

public sealed record HabitoDelDia(Guid Id, string Nombre, string? Descripcion, bool Cumplido, string? IconoId = null);
public sealed record HabitosHoy(DateOnly Fecha, HabitoDelDia[] Habitos);
public sealed record Cumplimiento(Guid Id, DateOnly Fecha);
public sealed record ResultadoHoy(HabitosHoy? Dia, string? Mensaje = null, string? Error = null, bool LoginRequerido = false);
public sealed record EnvioIncierto(Guid Habito, DateOnly Fecha, string Nombre);

public sealed class CumplirHabito(IMemoryCache cache)
{
    private readonly object accesoCache = new();
    private sealed class Envio
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public bool Incierto;
        public bool Confirmado;
        public string Nombre = "hábito";
        public readonly DateTimeOffset Expira = DateTimeOffset.UtcNow.AddDays(3);
    }

    private Envio ObtenerEnvio(Guid usuario, Guid habito, DateOnly fecha)
    {
        lock (accesoCache)
        {
            // shortcut: una instancia como las sesiones web; compartir antes de desplegar réplicas.
            var envios = cache.GetOrCreate(("cumplimientos-web", usuario), entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromDays(3);
                return new ConcurrentDictionary<(Guid, DateOnly), Envio>();
            })!;
            foreach (var item in envios)
                if (item.Value.Expira <= DateTimeOffset.UtcNow && item.Value.Gate.CurrentCount == 1)
                    envios.TryRemove(item.Key, out _);
            return envios.GetOrAdd((habito, fecha), _ => new Envio());
        }
    }

    private ConcurrentDictionary<(Guid Habito, DateOnly Fecha), Envio>? BuscarEnvios(Guid usuario) =>
        cache.TryGetValue(("cumplimientos-web", usuario),
            out ConcurrentDictionary<(Guid, DateOnly), Envio>? envios) ? envios : null;

    private Envio? BuscarEnvio(Guid usuario, Guid habito, DateOnly fecha) =>
        BuscarEnvios(usuario)?.TryGetValue((habito, fecha), out var envio) == true ? envio : null;

    public EnvioIncierto[] ListarInciertos(Guid usuario) => BuscarEnvios(usuario)?
        .Where(e => Volatile.Read(ref e.Value.Incierto))
        .Select(e => new EnvioIncierto(e.Key.Habito, e.Key.Fecha, e.Value.Nombre)).ToArray() ?? [];

    public bool EstaIncierto(Guid usuario, Guid habito, DateOnly fecha)
    {
        var envio = BuscarEnvio(usuario, habito, fecha);
        return envio is not null && Volatile.Read(ref envio.Incierto);
    }

    public async Task<ResultadoHoy> CargarAsync(HttpClient client, CancellationToken cancellation)
    {
        try
        {
            using var response = await client.GetAsync("api/habitos/diarios/hoy", cancellation);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var dia = await response.Content.ReadFromJsonAsync<HabitosHoy>(cancellation);
                if (dia is not null && dia.Fecha != default && dia.Habitos is not null &&
                    dia.Habitos.All(h => h is not null && h.Id != Guid.Empty && !string.IsNullOrWhiteSpace(h.Nombre)))
                    return new(dia);
            }
            return new(null, Error: MensajeError(response.StatusCode), LoginRequerido: response.StatusCode == HttpStatusCode.Unauthorized);
        }
        catch (Exception ex) when (EsErrorDeRespuesta(ex))
        {
            return new(null, Error: "No pudimos cargar tus hábitos de hoy. Volvé a intentar.");
        }
    }

    public async Task<ResultadoHoy> RegistrarAsync(
        HttpClient client, Guid usuario, Guid habito, DateOnly fecha, CancellationToken cancellation)
    {
        if (usuario == Guid.Empty || habito == Guid.Empty || fecha == default)
            return new(null, Error: "Revisá los datos del cumplimiento.");
        var envio = BuscarEnvio(usuario, habito, fecha);
        HabitoDelDia? candidato = null;
        if (envio is null || (!Volatile.Read(ref envio.Confirmado) && !Volatile.Read(ref envio.Incierto)))
        {
            var actual = await CargarAsync(client, cancellation);
            if (actual.Dia is not { } dia)
                return actual;
            if (dia.Fecha != fecha)
                return actual with { Mensaje = "Cambió la fecha local. Revisá el día actualizado y hacé una nueva activación." };
            candidato = dia.Habitos.SingleOrDefault(h => h.Id == habito);
            if (candidato is null)
                return actual with { Mensaje = "La acción ya no está disponible. Actualizamos tus hábitos." };
            if (candidato.Cumplido)
                return actual with { Mensaje = $"«{candidato.Nombre}» ya figura realizado para el {fecha:dd/MM/yyyy}." };
            envio = ObtenerEnvio(usuario, habito, fecha);
        }
        if (!await envio.Gate.WaitAsync(0, cancellation))
            return await RefrescarAsync(client, "Ya hay una solicitud para este hábito. Comprobá su estado antes de volver a intentarlo.", cancellation);
        try
        {
            if (envio.Confirmado)
                return await RefrescarAsync(client, Confirmacion(envio, fecha), cancellation);
            if (envio.Incierto)
                return await ComprobarAsync(client, envio, habito, fecha, false, cancellation);
            if (candidato is null)
                return await RefrescarAsync(client,
                    "El estado de la solicitud cambió. Revisá la pantalla y hacé una nueva activación.", cancellation);
            envio.Nombre = candidato.Nombre;
            envio.Incierto = true;
            try
            {
                using var response = await client.PostAsJsonAsync(
                    $"api/habitos/{habito}/cumplimientos", new { fecha }, cancellation);
                if (response.StatusCode == HttpStatusCode.Created)
                {
                    var registro = await response.Content.ReadFromJsonAsync<Cumplimiento>(cancellation);
                    if (registro is not null && registro.Id != Guid.Empty && registro.Fecha == fecha)
                    {
                        envio.Confirmado = true;
                        envio.Incierto = false;
                        return await RefrescarAsync(client, Confirmacion(envio, fecha), cancellation);
                    }
                }
                else if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or
                         HttpStatusCode.Unauthorized or HttpStatusCode.UnprocessableEntity or HttpStatusCode.ServiceUnavailable)
                {
                    envio.Incierto = false;
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        return new(null, "Tu sesión ya no es válida.", LoginRequerido: true);
                    return await RefrescarAsync(client, MensajeError(response.StatusCode), cancellation);
                }
                else if (response.StatusCode == HttpStatusCode.Conflict)
                    return await ComprobarAsync(client, envio, habito, fecha, true, cancellation);
            }
            catch (Exception ex) when (EsErrorDeRespuesta(ex))
            {
                // El servidor pudo guardar aunque la respuesta no llegara.
            }
            return await ComprobarAsync(client, envio, habito, fecha, false, cancellation);
        }
        finally { envio.Gate.Release(); }
    }

    private async Task<ResultadoHoy> ComprobarAsync(
        HttpClient client, Envio envio, Guid habito, DateOnly fecha, bool conflicto, CancellationToken cancellation)
    {
        try
        {
            using var response = await client.GetAsync($"api/habitos/{habito}/cumplimientos", cancellation);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new(null, "Tu sesión ya no es válida.", LoginRequerido: true);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var registros = await response.Content.ReadFromJsonAsync<Cumplimiento[]>(cancellation);
                if (registros is not null && registros.All(r => r is not null && r.Id != Guid.Empty && r.Fecha != default))
                {
                    envio.Confirmado = registros.Any(r => r.Fecha == fecha);
                    envio.Incierto = false;
                    return await RefrescarAsync(client, envio.Confirmado ? Confirmacion(envio, fecha) :
                        conflicto ? await MotivoConflictoAsync(client, habito, cancellation) :
                        "No hay un cumplimiento registrado para esa fecha. Podés intentarlo con una nueva activación.", cancellation);
                }
            }
        }
        catch (Exception ex) when (EsErrorDeRespuesta(ex))
        {
            // Mantener la incertidumbre hasta una lectura válida de los hechos persistidos.
        }
        return await RefrescarAsync(client,
            $"El resultado de «{envio.Nombre}» del {fecha:dd/MM/yyyy} sigue incierto. Comprobá el estado; todavía no podemos ofrecer otro envío.",
            cancellation);
    }

    private static async Task<string> MotivoConflictoAsync(HttpClient client, Guid habito, CancellationToken cancellation)
    {
        try
        {
            using var response = await client.GetAsync($"api/habitos/{habito}", cancellation);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var data = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
                if (data.RootElement.TryGetProperty("estado", out var estado) &&
                    estado.ValueKind == JsonValueKind.Number && estado.TryGetInt32(out var valor) && valor == 1)
                    // EstadoHabito.Inactivo se serializa como 1 en el contrato de detalle.
                    return "No hay un cumplimiento registrado para esa fecha. El hábito está inactivo y no admite nuevos registros.";
            }
        }
        catch (Exception ex) when (EsErrorDeRespuesta(ex)) { }
        return "La API rechazó el registro y no encontramos un cumplimiento para esa fecha. No pudimos confirmar el motivo; revisá los hábitos actualizados.";
    }

    private async Task<ResultadoHoy> RefrescarAsync(HttpClient client, string mensaje, CancellationToken cancellation) =>
        (await CargarAsync(client, cancellation)) with { Mensaje = mensaje };
    private static string Confirmacion(Envio envio, DateOnly fecha) =>
        $"Registro confirmado de «{envio.Nombre}» para el {fecha:dd/MM/yyyy}.";
    private static bool EsErrorDeRespuesta(Exception ex) =>
        ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException;
    private static string MensajeError(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => "La fecha o la programación ya no permite esta acción. Revisá tus hábitos.",
        HttpStatusCode.NotFound => "El hábito ya no está disponible.",
        HttpStatusCode.Unauthorized => "Tu sesión ya no es válida.",
        HttpStatusCode.UnprocessableEntity => "No se conoce el estado de uno o más hábitos para hoy.",
        HttpStatusCode.ServiceUnavailable => "Los hábitos de hoy todavía no están disponibles.",
        _ => "No pudimos cargar tus hábitos de hoy."
    };
}
