using System.Net.Http.Headers;
using System.Text.Json;
using Constia.Application.Correo;
namespace Constia.Infrastructure.Correo;

/// <summary>Un SendAsync por invocación. Sin acceso a persistencia, emisión ni endpoints públicos.</summary>
public sealed class ResendHttpTransport(HttpClient http, OpcionesResend opciones, TimeProvider clock)
    : ITransporteCorreoPreparado
{
    public const int MaxResponseBytes = 16 * 1024;
    public const string Endpoint = "https://api.resend.com/emails";

    public async Task<ResultadoEntregaCorreo> EnviarAsync(SolicitudCorreoPreparada solicitud, CancellationToken cancellationToken)
    {
        if (!opciones.Enabled)
            return new(EstadoEntregaCorreo.ProveedorNoDisponible, MotivoEntregaCorreo.Deshabilitado);
        if (ValidadorOpcionesResend.ValidarTransporte(opciones).Count != 0)
            return new(EstadoEntregaCorreo.ProveedorNoDisponible, MotivoEntregaCorreo.ConfiguracionInvalida);
        if (cancellationToken.IsCancellationRequested)
            return new(EstadoEntregaCorreo.CanceladaAntesDelEnvio, MotivoEntregaCorreo.Cancelacion);
        var bytes = solicitud.CopiarPayload();
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opciones.ApiKey);
        request.Headers.Add("Idempotency-Key", solicitud.ClaveIdempotencia);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(opciones.RequestTimeoutSeconds), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var iniciado = false;
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            iniciado = true; // No presume conocer si el servidor recibió bytes.
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var status = (int)response.StatusCode;
            var espera = Espera(response);
            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
                return Grande(status, espera);
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            var buffer = new byte[MaxResponseBytes + 1];
            var total = 0;
            while (total < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(total), linked.Token);
                if (n == 0) break;
                total += n;
            }
            if (total > MaxResponseBytes) return Grande(status, espera);
            return ClasificadorRespuestaResend.Clasificar(status, buffer.AsMemory(0, total), espera);
        }
        catch (OperationCanceledException)
        {
            return new(iniciado ? EstadoEntregaCorreo.ResultadoIncierto : EstadoEntregaCorreo.CanceladaAntesDelEnvio,
                cancellationToken.IsCancellationRequested ? MotivoEntregaCorreo.Cancelacion : MotivoEntregaCorreo.Timeout,
                intentoHttpIniciado: iniciado);
        }
        catch (HttpRequestException ex)
        {
            var (estado, motivo) = ex.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => (EstadoEntregaCorreo.FalloTemporal, MotivoEntregaCorreo.Dns),
                HttpRequestError.SecureConnectionError => (EstadoEntregaCorreo.ProveedorNoDisponible, MotivoEntregaCorreo.Tls),
                _ => (EstadoEntregaCorreo.ResultadoIncierto, MotivoEntregaCorreo.Red)
            };
            return new(estado, motivo, intentoHttpIniciado: iniciado);
        }
        catch (IOException)
        {
            return new(EstadoEntregaCorreo.ResultadoIncierto, MotivoEntregaCorreo.Red, intentoHttpIniciado: iniciado);
        }
    }

    private static ResultadoEntregaCorreo Grande(int status, TimeSpan? espera) =>
        new(EstadoEntregaCorreo.ResultadoIncierto, MotivoEntregaCorreo.RespuestaDemasiadoGrande,
            httpStatus: status, esperaMinima: espera, intentoHttpIniciado: true);

    private TimeSpan? Espera(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry?.Delta is TimeSpan delta) return delta >= TimeSpan.Zero ? delta : null;
        if (retry?.Date is DateTimeOffset date)
        {
            var span = date - clock.GetUtcNow();
            return span > TimeSpan.Zero ? span : TimeSpan.Zero;
        }
        return null;
    }
}
