namespace Constia.Application.Correo;

/// <summary>Secreto transitorio en memoria: nunca serializar ni registrar.</summary>
public sealed class TrabajoVerificacionCorreo
{
    public DateTimeOffset AdmitidoUtc { get; }
    public long MarcaAdmision { get; }
    public MensajeVerificacionCorreo? Mensaje { get; }
    public string? EmailReenvio { get; }
    private Action<FinTrabajoCorreo>? _finalizar;
    public TrabajoVerificacionCorreo(DateTimeOffset admitidoUtc, long marcaAdmision,
        MensajeVerificacionCorreo? mensaje = null, string? emailReenvio = null)
    {
        if ((mensaje is null) == (emailReenvio is null)) throw new ArgumentException("Trabajo inválido.");
        AdmitidoUtc = admitidoUtc; MarcaAdmision = marcaAdmision; Mensaje = mensaje; EmailReenvio = emailReenvio;
    }
    // La cola es dueña del cierre después de publicar.
    public void VincularFinalizacion(Action<FinTrabajoCorreo> finalizar)
    {
        if (Interlocked.CompareExchange(ref _finalizar, finalizar, null) is not null)
            throw new InvalidOperationException("Trabajo ya publicado.");
    }
    public void Finalizar(FinTrabajoCorreo motivo) => Interlocked.Exchange(ref _finalizar, null)?.Invoke(motivo);
    public override string ToString() => "[trabajo de correo redactado]";
}
