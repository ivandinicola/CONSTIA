using System.Net;
using Constia.Application.Correo;
using Constia.Application.Usuarios;
namespace Constia.Infrastructure.Correo;

/// <summary>Contenido técnico provisional, sin aprobación visual de plantilla definitiva.</summary>
public sealed class PreparadorMensajeVerificacion(OpcionesResend opciones) : IPreparadorMensajeVerificacion
{
    public MensajeVerificacionCorreo Preparar(Guid desafioId, string destinatario, string nombre, string token,
        DateTimeOffset emitidoEnUtc, DateTimeOffset expiraEnUtc)
    {
        if (ValidadorOpcionesResend.Validar(opciones).Count != 0)
            throw new InvalidOperationException("Configuración de correo inválida.");
        if (desafioId == Guid.Empty ||
            !HashTokenVerificacion.IntentarCalcular(token, out _))
            throw new ArgumentException("Datos de emisión inválidos.");
        var saludo = DatosVerificacionCorreo.NombreValido(nombre) ? $"Hola, {nombre}." : "Hola.";
        var origin = new Uri(opciones.PublicOrigin!).GetLeftPart(UriPartial.Authority);
        var enlace = $"{origin}/confirmar-correo#desafioId={desafioId:D}&token={Uri.EscapeDataString(token)}";
        var texto = $"CONSTIA — Tu constancia, día a día.\n\n{saludo}\nConfirmá tu correo.\n" +
            "Al abrir el enlace te pediremos la contraseña que elegiste al registrarte.\n" +
            $"Confirmar mi correo: {enlace}\nEste enlace vence 24 horas después de su emisión.\n" +
            "Si no creaste esta cuenta, ignorá este mensaje. No compartas el enlace ni tu contraseña.";
        var html = "<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"></head><body>" +
            "<p>CONSTIA — Tu constancia, día a día.</p><h1>Confirmá tu correo</h1>" +
            $"<p>{WebUtility.HtmlEncode(saludo)}</p>" +
            "<p>Al abrir el enlace te pediremos la contraseña que elegiste al registrarte.</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(enlace)}\">Confirmar mi correo</a></p>" +
            "<p>Este enlace vence 24 horas después de su emisión.</p>" +
            "<p>Si no creaste esta cuenta, ignorá este mensaje. No compartas el enlace ni tu contraseña.</p></body></html>";
        return new(desafioId, destinatario, opciones.FromAddress!, opciones.FromName, "Confirmá tu correo",
            html, texto, emitidoEnUtc, expiraEnUtc);
    }
}
