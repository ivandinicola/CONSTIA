namespace Constia.Application.Correo;
/// <summary>Snapshot defensivo. Puede contener datos sensibles; nunca registrar ni persistir desde el transporte.</summary>
public sealed class SolicitudCorreoPreparada
{
    private readonly byte[] _payload;
    public string ClaveIdempotencia { get; }
    public SolicitudCorreoPreparada(string claveIdempotencia, byte[] payload)
    {
        if (string.IsNullOrWhiteSpace(claveIdempotencia) || claveIdempotencia.Length > 256 || claveIdempotencia.Any(char.IsControl))
            throw new ArgumentException("Clave inválida.");
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0) throw new ArgumentException("Payload vacío.");
        ClaveIdempotencia = claveIdempotencia; _payload = (byte[])payload.Clone();
    }
    public byte[] CopiarPayload() => (byte[])_payload.Clone();
    public override string ToString() => "[solicitud de correo redactada]";
}
