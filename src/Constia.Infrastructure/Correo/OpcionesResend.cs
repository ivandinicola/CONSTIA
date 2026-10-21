namespace Constia.Infrastructure.Correo;

/// <summary>Instantánea backend. Nunca registrar ni enviar esta configuración al navegador.</summary>
public sealed class OpcionesResend
{
    public bool Enabled { get; init; }
    public string? ApiKey { get; init; }
    public string? FromAddress { get; init; }
    public string FromName { get; init; } = "CONSTIA";
    public string? PublicOrigin { get; init; }
    public int RequestTimeoutSeconds { get; init; } = 10;
    public override string ToString() => "[configuración Resend redactada]";
}
