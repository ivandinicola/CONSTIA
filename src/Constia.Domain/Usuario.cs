namespace Constia.Domain;

public class Usuario
{
    public Guid Id { get; private set; }

    public string Nombre { get; private set; }

    public string Email { get; private set; }

    public string PasswordHash { get; private set; }

    public Usuario(string nombre, string email, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        Id = Guid.NewGuid();
        Nombre = nombre;
        Email = email;
        PasswordHash = passwordHash;
    }
}
