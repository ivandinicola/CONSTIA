using System.Security.Cryptography;
using System.Text;
using Constia.Application.Correo;

namespace Constia.Infrastructure.Correo;

public static class IdentidadDestinatarioEnsayo
{
    // Exact UTF-8 bytes: authorization and consumption must use the same canonical recipient.
    public static byte[] Calcular(string destinatario, ReadOnlySpan<byte> clave)
    {
        if (!DatosVerificacionCorreo.DireccionSimple(destinatario) || clave.Length < 32)
            throw new ArgumentException("Identidad o clave inválida.");
        return HMACSHA256.HashData(clave, Encoding.UTF8.GetBytes(destinatario));
    }
}
