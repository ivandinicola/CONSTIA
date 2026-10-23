namespace Constia.Infrastructure.Bienvenida;
/// <summary>Estructura persistente futura. No existe API de reserva en C3-A.</summary>
public sealed class IntentoBienvenida
{
    public Guid Id { get; private set; }
    public Guid EventoId { get; private set; }
    public byte Ordinal { get; private set; }
    public DateTimeOffset ReservadoEnUtc { get; private set; }
    public Guid PropietarioLeaseId { get; private set; }
    public long LeaseGeneracion { get; private set; }
    public EstadoIntentoBienvenida Estado { get; private set; }
    public bool? HttpIniciadoConfirmado { get; private set; }
    public int? MotivoEntrega { get; private set; }
    public short? HttpStatus { get; private set; }
    public DateTimeOffset? FinalizadoEnUtc { get; private set; }
    private IntentoBienvenida() { }
    public override string ToString() => "[intento de bienvenida redactado]";
}
