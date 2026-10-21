namespace Constia.Domain;

public sealed class DesafioVerificacionCorreo
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string EmailDestino { get; private set; } = null!;
    public byte Proposito { get; private set; } = 1;
    public byte[] TokenHash { get; private set; } = null!;
    public DateTimeOffset EmitidoEnUtc { get; private set; }
    public DateTimeOffset ExpiraEnUtc { get; private set; }
    public DateTimeOffset? ConsumidoEnUtc { get; private set; }
    public DateTimeOffset? InvalidadoEnUtc { get; private set; }
    public DateTimeOffset? VentanaIntentosEnUtc { get; private set; }
    public int IntentosConfirmacion { get; private set; }

    private DesafioVerificacionCorreo() { }

    public DesafioVerificacionCorreo(Guid usuarioId, string emailDestino, byte[] tokenHash, DateTimeOffset emitidoEnUtc)
    {
        if (usuarioId == Guid.Empty) throw new ArgumentException("Usuario requerido.", nameof(usuarioId));
        ArgumentException.ThrowIfNullOrWhiteSpace(emailDestino);
        ArgumentNullException.ThrowIfNull(tokenHash);
        if (tokenHash.Length != 32) throw new ArgumentException("Hash inválido.", nameof(tokenHash));
        if (emitidoEnUtc.Offset != TimeSpan.Zero) throw new ArgumentException("La fecha debe ser UTC.", nameof(emitidoEnUtc));
        Id = Guid.NewGuid(); UsuarioId = usuarioId; EmailDestino = emailDestino;
        TokenHash = tokenHash.ToArray(); EmitidoEnUtc = emitidoEnUtc; ExpiraEnUtc = emitidoEnUtc.AddHours(24);
    }

    public bool EstaVigente(DateTimeOffset ahora) =>
        ahora >= EmitidoEnUtc && ahora < ExpiraEnUtc && ConsumidoEnUtc is null && InvalidadoEnUtc is null;

    public bool RegistrarIntento(DateTimeOffset ahora, int maximo, TimeSpan ventana)
    {
        if (ahora.Offset != TimeSpan.Zero || maximo <= 0 || ventana <= TimeSpan.Zero)
            throw new ArgumentException("Política de intentos inválida.");
        if (VentanaIntentosEnUtc is null || ahora >= VentanaIntentosEnUtc.Value.Add(ventana))
        { VentanaIntentosEnUtc = ahora; IntentosConfirmacion = 0; }
        if (IntentosConfirmacion >= maximo) return false;
        IntentosConfirmacion++;
        return true;
    }

    public void Consumir(DateTimeOffset ahora)
    {
        if (ahora.Offset != TimeSpan.Zero || !EstaVigente(ahora)) throw new InvalidOperationException("Desafío no vigente.");
        ConsumidoEnUtc = ahora;
    }

    public void Invalidar(DateTimeOffset ahora)
    {
        if (ahora.Offset != TimeSpan.Zero || ahora < EmitidoEnUtc || ConsumidoEnUtc is not null || InvalidadoEnUtc is not null)
            throw new InvalidOperationException("Desafío no reemplazable.");
        InvalidadoEnUtc = ahora;
    }
}
