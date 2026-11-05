namespace Constia.Infrastructure.Correo;

public enum EstadoAutorizacionEnvio { Preparada = 0, Consumida = 1, Aceptada = 2, Incierta = 3 }

public sealed class AutorizacionEnvioUnico
{
    private byte[] _identidadDestinatario = [];
    private AutorizacionEnvioUnico() { }
    public AutorizacionEnvioUnico(Guid ensayoId, byte[] identidadDestinatario, Guid operacionId, DateTimeOffset creadaEnUtc)
    {
        if (ensayoId == Guid.Empty || operacionId == Guid.Empty || identidadDestinatario is not { Length: 32 } || creadaEnUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Identidad de autorización inválida.");
        EnsayoId = ensayoId; OperacionId = operacionId; _identidadDestinatario = (byte[])identidadDestinatario.Clone(); CreadaEnUtc = creadaEnUtc;
    }
    public Guid EnsayoId { get; private set; }
    public Guid OperacionId { get; private set; }
    public byte[] IdentidadDestinatario => (byte[])_identidadDestinatario.Clone();
    public EstadoAutorizacionEnvio Estado { get; private set; }
    public DateTimeOffset CreadaEnUtc { get; private set; }
    public DateTimeOffset? ConsumidaEnUtc { get; private set; }
    public DateTimeOffset? ResultadoEnUtc { get; private set; }
}
