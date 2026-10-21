using System.Net.Mail;
namespace Constia.Application.Correo;

/// <summary>Instantánea sensible sólo en memoria. Nunca serializar en logs ni persistir.</summary>
public sealed class MensajeVerificacionCorreo
{
    public Guid DesafioId { get; }
    public string Destinatario { get; }
    public string Remitente { get; }
    public string NombreRemitente { get; }
    public string Asunto { get; }
    public string Html { get; }
    public string TextoPlano { get; }
    public DateTimeOffset EmitidoEnUtc { get; }
    public DateTimeOffset ExpiraEnUtc { get; }
    public string ClaveIdempotencia => $"constia/verificacion/{DesafioId:D}";

    public MensajeVerificacionCorreo(Guid desafioId, string destinatario, string remitente, string nombreRemitente,
        string asunto, string html, string textoPlano, DateTimeOffset emitidoEnUtc, DateTimeOffset expiraEnUtc)
    {
        if (desafioId == Guid.Empty || !DireccionSimple(destinatario) || !DireccionSimple(remitente) ||
            nombreRemitente != "CONSTIA" || string.IsNullOrWhiteSpace(asunto) ||
            asunto.Any(char.IsControl) || asunto.Length > 200 || string.IsNullOrWhiteSpace(html) ||
            string.IsNullOrWhiteSpace(textoPlano) || emitidoEnUtc.Offset != TimeSpan.Zero ||
            expiraEnUtc.Offset != TimeSpan.Zero || expiraEnUtc - emitidoEnUtc != TimeSpan.FromHours(24))
            throw new ArgumentException("Mensaje de verificación inválido.");
        DesafioId = desafioId; Destinatario = destinatario; Remitente = remitente; NombreRemitente = nombreRemitente;
        Asunto = asunto; Html = html; TextoPlano = textoPlano; EmitidoEnUtc = emitidoEnUtc; ExpiraEnUtc = expiraEnUtc;
    }

    public static bool DireccionSimple(string? valor) =>
        !string.IsNullOrWhiteSpace(valor) && !valor.Any(char.IsControl) &&
        MailAddress.TryCreate(valor, out var address) && address.DisplayName.Length == 0 &&
        string.Equals(address.Address, valor, StringComparison.Ordinal);

    public override string ToString() => "[mensaje de verificación redactado]";
}
