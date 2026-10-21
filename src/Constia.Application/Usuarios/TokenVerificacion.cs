using System.Security.Cryptography;

namespace Constia.Application.Usuarios;

public sealed record TokenVerificacion(string Valor, byte[] Hash)
{
    public override string ToString() => "[token de verificación redactado]";
}

public interface IGeneradorTokenVerificacion
{
    TokenVerificacion Generar();
}

public static class HashTokenVerificacion
{
    public static bool IntentarCalcular(string? valor, out byte[] hash)
    {
        hash = [];
        if (valor is null || valor.Length != 43 || valor.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return false;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(valor.Replace('-', '+').Replace('_', '/') + "="); }
        catch (FormatException) { return false; }
        if (bytes.Length != 32 || Codificar(bytes) != valor) return false;
        hash = SHA256.HashData(bytes);
        CryptographicOperations.ZeroMemory(bytes);
        return true;
    }
    public static string Codificar(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static bool Coincide(byte[] esperado, byte[] recibido) =>
        esperado.Length == 32 && recibido.Length == 32 && CryptographicOperations.FixedTimeEquals(esperado, recibido);
}
