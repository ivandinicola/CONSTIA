using System.Security.Cryptography;
using Constia.Application.Usuarios;

namespace Constia.Infrastructure.Security;

public sealed class GeneradorTokenVerificacion : IGeneradorTokenVerificacion
{
    public TokenVerificacion Generar()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try { return new(HashTokenVerificacion.Codificar(bytes), SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
