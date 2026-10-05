namespace Constia.Application.Usuarios;

public sealed class AutenticarUsuario(
    IUsuarioRepository usuarios,
    IUsuarioPasswordHasher passwordHasher,
    IAccessTokenService accessTokenService)
{
    /// <summary>
    /// Returns null for any invalid credentials.
    /// </summary>
    public async Task<UsuarioAutenticado?> EjecutarAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var usuario = await usuarios.BuscarPorEmailAsync(email, cancellationToken);
        if (usuario is null || !passwordHasher.VerifyPassword(usuario.PasswordHash, password))
        {
            return null;
        }

        var token = accessTokenService.Emitir(usuario.Id, usuario.Nombre, usuario.Email);

        return new UsuarioAutenticado(
            usuario.Id,
            usuario.Nombre,
            usuario.Email,
            token.Value,
            token.ExpiresAt);
    }
}
