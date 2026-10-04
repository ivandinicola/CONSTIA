using Constia.Application.Usuarios;
using Microsoft.AspNetCore.Identity;

namespace Constia.Infrastructure.Security;

public sealed class AspNetCorePasswordHasher : IUsuarioPasswordHasher
{
    private static readonly object UserContext = new();
    private readonly PasswordHasher<object> _passwordHasher = new();

    public string HashPassword(string password)
    {
        return _passwordHasher.HashPassword(UserContext, password);
    }

    public bool VerifyPassword(string passwordHash, string password)
    {
        return _passwordHasher.VerifyHashedPassword(UserContext, passwordHash, password)
            != PasswordVerificationResult.Failed;
    }
}
