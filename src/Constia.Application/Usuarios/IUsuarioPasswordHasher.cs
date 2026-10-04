namespace Constia.Application.Usuarios;

public interface IUsuarioPasswordHasher
{
    string HashPassword(string password);

    bool VerifyPassword(string passwordHash, string password);
}
