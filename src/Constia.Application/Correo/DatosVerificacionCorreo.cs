using System.Net.Mail;
namespace Constia.Application.Correo;

public static class DatosVerificacionCorreo
{
    public const int MaximoNombre = 200;
    public const int MaximoEmail = 254;
    public static bool NombreValido(string? nombre) => !string.IsNullOrWhiteSpace(nombre) &&
        nombre.Length <= MaximoNombre && !nombre.Any(char.IsControl);
    public static bool DireccionSimple(string? email) => !string.IsNullOrWhiteSpace(email) &&
        email.Length <= MaximoEmail && !email.Any(char.IsControl) &&
        MailAddress.TryCreate(email, out var address) && address.DisplayName.Length == 0 &&
        string.Equals(address.Address, email, StringComparison.Ordinal);
    public static void ValidarAlta(string nombre, string email)
    {
        if (!NombreValido(nombre)) throw new DatosCorreoInvalidosException("Nombre");
        if (!DireccionSimple(email)) throw new DatosCorreoInvalidosException("Email");
    }
}
public sealed class DatosCorreoInvalidosException(string campo) : ArgumentException("Datos de registro inválidos.")
{
    public string Campo { get; } = campo;
}
