namespace Constia.Domain;

public class Usuario
{
    public Guid Id { get; private set; }

    public string Nombre { get; private set; }

    public string Email { get; private set; }

    public string PasswordHash { get; private set; }

    public string TimeZoneId { get; private set; }

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
