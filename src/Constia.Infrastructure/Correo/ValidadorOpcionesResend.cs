using Constia.Application.Correo;
namespace Constia.Infrastructure.Correo;

public static class ValidadorOpcionesResend
{
    public static IReadOnlyList<string> Validar(OpcionesResend opciones)
    {
        var errores = ValidarTransporte(opciones).Where(x=>x!="TimeoutFueraDeRango").ToList();
        if (!Uri.TryCreate(opciones.PublicOrigin, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0 ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath != "/" || opciones.PublicOrigin!.Any(char.IsControl))
            errores.Add("OrigenInseguro");

        if (opciones.RequestTimeoutSeconds is < 1 or > 30) errores.Add("TimeoutFueraDeRango");
        return errores.AsReadOnly();
    }
    public static IReadOnlyList<string> ValidarTransporte(OpcionesResend opciones)
    {
        var errores = new List<string>();
        if (string.IsNullOrWhiteSpace(opciones.ApiKey) || opciones.ApiKey.Length > 512 ||
            opciones.ApiKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c > 127))
            errores.Add("ApiKeyInvalida");
        if (!MensajeVerificacionCorreo.DireccionSimple(opciones.FromAddress)) errores.Add("RemitenteInvalido");
        if (opciones.FromName != "CONSTIA") errores.Add("NombreRemitenteInvalido");
        if (opciones.RequestTimeoutSeconds is < 1 or > 30) errores.Add("TimeoutFueraDeRango");
        return errores.AsReadOnly();
    }
}
