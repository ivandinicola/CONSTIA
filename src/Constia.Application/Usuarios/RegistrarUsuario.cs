using Constia.Domain;
using Constia.Application.Temporal;

namespace Constia.Application.Usuarios;

public sealed class RegistrarUsuario(
    IUsuarioRepository usuarios,
    IUsuarioPasswordHasher passwordHasher,
    IZonaHorariaIana zonasHorarias,
    IRegistroUsuarioCorreo correo)
{
    /// <summary>
    /// Returns null when the email is already registered, including a concurrent insert conflict.
    /// </summary>
    public async Task<UsuarioRegistrado?> EjecutarAsync(
        string nombre,
        string email,
        string password,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        if (!await correo.DisponibleAsync(cancellationToken)) throw new CorreoNoDisponibleException();
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        Constia.Application.Correo.DatosVerificacionCorreo.ValidarAlta(nombre, email);
        PoliticaContrasenaRegistro.Validar(password);

        if (!zonasHorarias.EsValida(timeZoneId))
        {
            throw new ZonaHorariaIanaInvalidaException(timeZoneId);
        }

        if (await usuarios.ExistePorEmailAsync(email, cancellationToken))
        {
            return null;
        }

        var usuario = new Usuario(nombre, email, passwordHasher.HashPassword(password), timeZoneId);

        if (!await correo.RegistrarAsync(usuario, cancellationToken))
        {
            return null;
        }

        return new UsuarioRegistrado(usuario.Id, usuario.Nombre, usuario.Email);
    }
}
