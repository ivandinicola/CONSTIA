using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Constia.Application.Usuarios;
using Microsoft.IdentityModel.Tokens;

namespace Constia.Infrastructure.Security;

public sealed class JwtAccessTokenService(JwtSettings settings) : IAccessTokenService
{
    public AccessToken Emitir(Guid userId, string name, string email)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(settings.ExpirationMinutes);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));
        var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("name", name),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Iat, issuedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: signingCredentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
