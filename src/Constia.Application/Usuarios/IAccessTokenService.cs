namespace Constia.Application.Usuarios;

public interface IAccessTokenService
{
    AccessToken Emitir(Guid userId, string name, string email);
}
