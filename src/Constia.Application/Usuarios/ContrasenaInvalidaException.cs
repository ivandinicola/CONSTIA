namespace Constia.Application.Usuarios;

/// <summary>Errores públicos de la contraseña de registro, sin conservar su valor.</summary>
public sealed class ContrasenaInvalidaException : ArgumentException
{
    public IReadOnlyList<string> Errores { get; }

    internal ContrasenaInvalidaException(params string[] errores)
        : base("La contraseña no cumple la política de registro.", "password")
    {
        Errores = Array.AsReadOnly(errores);
    }
}
