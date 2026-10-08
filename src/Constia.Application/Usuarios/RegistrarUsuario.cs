using Constia.Domain;
using Constia.Application.Temporal;

namespace Constia.Application.Usuarios;

public sealed class RegistrarUsuario(
    IUsuarioRepository usuarios,
    IUsuarioPasswordHasher passwordHasher,
    IZonaHorariaIana zonasHorarias)
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
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (!zonasHorarias.EsValida(timeZoneId))
        {
            throw new ZonaHorariaIanaInvalidaException(timeZoneId);
        }

        if (await usuarios.ExistePorEmailAsync(email, cancellationToken))
        {
            return null;
        }

        var usuario = new Usuario(nombre, email, passwordHasher.HashPassword(password), timeZoneId);

        if (!await usuarios.IntentarAgregarAsync(usuario, cancellationToken))
        {
            return null;
        }

        return new UsuarioRegistrado(usuario.Id, usuario.Nombre, usuario.Email);
    }
}
