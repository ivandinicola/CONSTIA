using Constia.Domain;

namespace Constia.Application.Usuarios;

public sealed class RegistrarUsuario(
    IUsuarioRepository usuarios,
    IUsuarioPasswordHasher passwordHasher)
{
    /// <summary>
    /// Returns null when the email is already registered, including a concurrent insert conflict.
    /// </summary>
    public async Task<UsuarioRegistrado?> EjecutarAsync(
        string nombre,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (await usuarios.ExistePorEmailAsync(email, cancellationToken))
        {
            return null;
        }

        var usuario = new Usuario(nombre, email, passwordHasher.HashPassword(password));

        if (!await usuarios.IntentarAgregarAsync(usuario, cancellationToken))
        {
            return null;
        }

        return new UsuarioRegistrado(usuario.Id, usuario.Nombre, usuario.Email);
    }
}
