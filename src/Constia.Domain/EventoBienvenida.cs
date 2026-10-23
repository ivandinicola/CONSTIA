namespace Constia.Domain;
/// <summary>Evidencia de una bienvenida lógica. C3-A sólo permite su captura pendiente.</summary>
public sealed class EventoBienvenida
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public Guid EpocaCaptura { get; private set; }
    public byte Tipo { get; private set; } = 1;
    public DateTimeOffset ConfirmadoEnUtc { get; private set; }
    public DateTimeOffset CapturadoEnUtc { get; private set; }
    public DateTimeOffset ExpiraEnUtc { get; private set; }
    public EstadoEventoBienvenida Estado { get; private set; }
    public MotivoFinalBienvenida MotivoFinal { get; private set; }
    public int Reservas { get; private set; }
    public bool HuboIncertidumbre { get; private set; }
    public DateTimeOffset? ProximaEjecucionEnUtc { get; private set; }
    public DateTimeOffset? PrimeraReservaEnUtc { get; private set; }
    public DateTimeOffset? HorizonteHttpEnUtc { get; private set; }
    public Guid? LeaseId { get; private set; }
    public long LeaseGeneracion { get; private set; }
    public DateTimeOffset? LeaseHastaUtc { get; private set; }
    public DateTimeOffset? UltimoLeaseHastaUtc { get; private set; }
    public string? Destinatario { get; private set; }
    public string PlantillaVersion { get; private set; } = "bienvenida-v1";
    public string? PerfilProveedorVersion { get; private set; }
    public byte[]? PayloadUtf8 { get; private set; }
    public byte[]? PayloadSha256 { get; private set; }
    public string? IdProveedor { get; private set; }
    public DateTimeOffset? FinalizadoEnUtc { get; private set; }
    public DateTimeOffset? DatosDepuradosEnUtc { get; private set; }
    public string ClaveIdempotencia => $"constia/bienvenida/{Id:D}";
    private EventoBienvenida() { }
    public static EventoBienvenida CrearPendiente(Guid usuarioId, Guid epoca, string destinatario,
        DateTimeOffset confirmadoUtc, DateTimeOffset capturadoUtc)
    {
        if(usuarioId==Guid.Empty || epoca==Guid.Empty) throw new ArgumentException("Identidad de bienvenida inválida.");
        ArgumentNullException.ThrowIfNull(destinatario);
        if(confirmadoUtc.Offset!=TimeSpan.Zero || capturadoUtc.Offset!=TimeSpan.Zero)
            throw new ArgumentException("Datos de captura inválidos.");
        return new() { Id=Guid.NewGuid(), UsuarioId=usuarioId, EpocaCaptura=epoca,
            Destinatario=destinatario, ConfirmadoEnUtc=confirmadoUtc, CapturadoEnUtc=capturadoUtc,
            ExpiraEnUtc=capturadoUtc.AddHours(24), Estado=EstadoEventoBienvenida.Pendiente,
            ProximaEjecucionEnUtc=capturadoUtc };
    }
    public override string ToString() => "[evento de bienvenida redactado]";
}
