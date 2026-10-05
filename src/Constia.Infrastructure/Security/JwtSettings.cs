using System.Text;

namespace Constia.Infrastructure.Security;

public sealed class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public int ExpirationMinutes { get; set; }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SigningKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(Audience);

        if (Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "JWT signing key must contain at least 32 UTF-8 bytes for HS256.");
        }

        if (ExpirationMinutes <= 0)
        {
            throw new InvalidOperationException("JWT expiration minutes must be greater than zero.");
        }
    }
}
