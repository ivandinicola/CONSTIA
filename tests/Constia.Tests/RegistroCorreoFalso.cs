using Constia.Application.Usuarios;
using Constia.Domain;
namespace Constia.Tests;

// Fixture de persistencia aislada para las pruebas de contrato de registro existentes.
internal sealed class RegistroCorreoFalso(IUsuarioRepository usuarios) : IRegistroUsuarioCorreo
{
    public Task<bool> DisponibleAsync(CancellationToken ct) => Task.FromResult(true);
    public Task<bool> RegistrarAsync(Usuario usuario, CancellationToken ct) => usuarios.IntentarAgregarAsync(usuario, ct);
}
