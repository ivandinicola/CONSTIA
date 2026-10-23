namespace Constia.Infrastructure.Bienvenida;
public sealed class ControlBienvenida
{
    public int Id { get; private set; } = 1;
    public int VersionEsquema { get; private set; } = 1;
    public int ProtocoloEscritor { get; private set; } = 1;
    public bool CapturaHabilitada { get; private set; }
    public Guid? EpocaCaptura { get; private set; }
    public DateTimeOffset? InicioCapturaEnUtc { get; private set; }
    public DateTimeOffset? FinCapturaEnUtc { get; private set; }
    public bool EnvioHabilitado { get; private set; }
    public bool PausaProveedor { get; private set; }
    public int? MotivoPausa { get; private set; }
    private ControlBienvenida() { }
    public override string ToString() => "[control de bienvenida redactado]";
}
