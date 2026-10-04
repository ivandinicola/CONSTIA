using Constia.Domain;

namespace Constia.Application.Usuarios;

public interface IUsuarioRepository
{
    Task<bool> ExistePorEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the user and returns false only when the unique email index rejects it.
    /// </summary>
    Task<bool> IntentarAgregarAsync(Usuario usuario, CancellationToken cancellationToken);
}
