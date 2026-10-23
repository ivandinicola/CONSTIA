using System.Net.Http.Headers;
using System.Text.Json;
using Constia.Application.Correo;
namespace Constia.Infrastructure.Correo;

/// <summary>Un SendAsync por invocación. Sin acceso a persistencia, emisión ni endpoints públicos.</summary>
public sealed class ResendEmailClient(HttpClient http, OpcionesResend opciones, TimeProvider clock)
    : ITransporteCorreoVerificacion
{
    public const int MaxResponseBytes = 16 * 1024;
    public const string Endpoint = "https://api.resend.com/emails";

    public async Task<ResultadoEntregaCorreo> EnviarAsync(MensajeVerificacionCorreo mensaje, CancellationToken cancellationToken)
    {
        if (!opciones.Enabled)
            return new(EstadoEntregaCorreo.ProveedorNoDisponible, MotivoEntregaCorreo.Deshabilitado);
        if (ValidadorOpcionesResend.Validar(opciones).Count != 0)
            return new(EstadoEntregaCorreo.ProveedorNoDisponible, MotivoEntregaCorreo.ConfiguracionInvalida);
        if (mensaje.Remitente != opciones.FromAddress || mensaje.NombreRemitente != opciones.FromName)
            return new(EstadoEntregaCorreo.RechazadaDefinitivamente, MotivoEntregaCorreo.MensajeInvalido);
        if (cancellationToken.IsCancellationRequested)
            return new(EstadoEntregaCorreo.CanceladaAntesDelEnvio, MotivoEntregaCorreo.Cancelacion);

        // Propiedades en orden fijo, sin reloj, valores aleatorios ni opciones JSON globales.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            from = $"{mensaje.NombreRemitente} <{mensaje.Remitente}>",
            to = new[] { mensaje.Destinatario }, subject = mensaje.Asunto, html = mensaje.Html, text = mensaje.TextoPlano
        });
        return await new ResendHttpTransport(http, opciones, clock).EnviarAsync(
            new SolicitudCorreoPreparada(mensaje.ClaveIdempotencia, bytes), cancellationToken);
    }
}
