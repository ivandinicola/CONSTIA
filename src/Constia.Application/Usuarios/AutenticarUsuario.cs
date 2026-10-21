namespace Constia.Application.Usuarios;

public sealed class AutenticarUsuario(
    IUsuarioRepository usuarios,
    IUsuarioPasswordHasher passwordHasher,
    IAccessTokenService accessTokenService)
{
    /// <summary>
    /// Returns an explicit result; pending accounts never invoke the JWT issuer.
    /// </summary>
    public async Task<ResultadoAutenticacion> EjecutarAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var usuario = await usuarios.BuscarPorEmailAsync(email, cancellationToken);
        if (usuario is null || !passwordHasher.VerifyPassword(usuario.PasswordHash, password))
        {
            return new ResultadoAutenticacion(EstadoAutenticacion.CredencialesInvalidas);
        }

        if (usuario.EstadoVerificacionCorreo == Constia.Domain.EstadoVerificacionCorreo.Pendiente)
            return new ResultadoAutenticacion(EstadoAutenticacion.VerificacionRequerida);

        if (usuario.EstadoVerificacionCorreo is not (Constia.Domain.EstadoVerificacionCorreo.Verificado or Constia.Domain.EstadoVerificacionCorreo.LegadoExento))
            return new ResultadoAutenticacion(EstadoAutenticacion.CredencialesInvalidas);

        var token = accessTokenService.Emitir(usuario.Id, usuario.Nombre, usuario.Email);

        return new ResultadoAutenticacion(EstadoAutenticacion.Autenticado, new UsuarioAutenticado(
            usuario.Id,
            usuario.Nombre,
            usuario.Email,
            token.Value,
            token.ExpiresAt));
    }
}
