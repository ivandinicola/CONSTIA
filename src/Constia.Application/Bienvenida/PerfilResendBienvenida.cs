using System.Security.Cryptography;
using System.Text.Json;
using Constia.Application.Correo;
namespace Constia.Application.Bienvenida;
public sealed class PerfilResendBienvenida
{
    public const string Endpoint = "https://api.resend.com/emails";
    public string Cuenta { get; }
    public string Remitente { get; }
    public string Huella { get; }
    public PerfilResendBienvenida(string cuenta, string remitente)
    {
        if (string.IsNullOrEmpty(cuenta) || cuenta.Length > 128 ||
            cuenta.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            throw new ArgumentException("Referencia de cuenta inválida.");
        if (!DatosVerificacionCorreo.DireccionSimple(remitente)) throw new ArgumentException("Remitente inválido.");
        Cuenta = cuenta; Remitente = remitente;
        Huella = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            version = 1, proveedor = "resend", endpoint = Endpoint, cuenta, remitente,
            nombre = "CONSTIA", contrato = "emails-json-v1" })));
    }
    public override string ToString() => "[perfil redactado]";
}
