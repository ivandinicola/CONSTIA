namespace Constia.Domain;

public class Usuario
{
    public Guid Id { get; private set; }

    public string Nombre { get; private set; }

    public string Email { get; private set; }

    public string PasswordHash { get; private set; }

    public string TimeZoneId { get; private set; }

    public EstadoVerificacionCorreo EstadoVerificacionCorreo { get; private set; } = EstadoVerificacionCorreo.Pendiente;

    public DateTimeOffset? EmailVerificadoEnUtc { get; private set; }

    public void ConfirmarCorreo(DateTimeOffset confirmadoEnUtc)
    {
        if (EstadoVerificacionCorreo != EstadoVerificacionCorreo.Pendiente)
            throw new InvalidOperationException("La cuenta no está pendiente de confirmación.");
        if (confirmadoEnUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("La fecha debe ser UTC.", nameof(confirmadoEnUtc));
        EstadoVerificacionCorreo = EstadoVerificacionCorreo.Verificado;
        EmailVerificadoEnUtc = confirmadoEnUtc;
    }

    public Usuario(string nombre, string email, string passwordHash, string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        Id = Guid.NewGuid();
        Nombre = nombre;
        Email = email;
        PasswordHash = passwordHash;
        TimeZoneId = timeZoneId;
    }

    public void CambiarZonaHoraria(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        TimeZoneId = timeZoneId;
    }
}
