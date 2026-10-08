using Constia.Application.Usuarios;
using Constia.Domain;

namespace Constia.Tests;

public class AutenticarUsuarioTests
{
    private const string PasswordDePrueba = "clave-de-prueba-no-real";

    [Fact]
    public async Task EjecutarAsync_ConCredencialesCorrectas_DevuelveDatosPublicosYVerificaHash()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hash-seguro", "Etc/UTC");
        var usuarios = new FakeUsuarioRepository(usuario);
        var passwordHasher = new FakePasswordHasher(resultadoVerificacion: true);
        var accessTokenService = new FakeAccessTokenService();
        var casoDeUso = new AutenticarUsuario(usuarios, passwordHasher, accessTokenService);

        var resultado = await casoDeUso.EjecutarAsync(usuario.Email, PasswordDePrueba);

        Assert.Equal(
            new UsuarioAutenticado(
                usuario.Id,
                usuario.Nombre,
                usuario.Email,
                "token-ficticio-de-prueba",
                accessTokenService.ExpiresAt),
            resultado);
        Assert.Equal((usuario.PasswordHash, PasswordDePrueba), passwordHasher.ArgumentosVerificacion);
        Assert.Equal((usuario.Id, usuario.Nombre, usuario.Email), accessTokenService.ArgumentosEmision);
        Assert.DoesNotContain(
            typeof(UsuarioAutenticado).GetProperties(),
            propiedad => propiedad.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase)
                || propiedad.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EjecutarAsync_CuandoEmailNoExiste_DevuelveFalloComunSinVerificarPassword()
    {
        var passwordHasher = new FakePasswordHasher(resultadoVerificacion: true);
        var accessTokenService = new FakeAccessTokenService();
        var casoDeUso = new AutenticarUsuario(new FakeUsuarioRepository(null), passwordHasher, accessTokenService);

        var resultado = await casoDeUso.EjecutarAsync("ausente@example.com", PasswordDePrueba);

        Assert.Null(resultado);
        Assert.Null(passwordHasher.ArgumentosVerificacion);
        Assert.Null(accessTokenService.ArgumentosEmision);
    }

    [Fact]
    public async Task EjecutarAsync_CuandoPasswordEsIncorrecta_DevuelveFalloComun()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hash-seguro", "Etc/UTC");
        var passwordHasher = new FakePasswordHasher(resultadoVerificacion: false);
        var accessTokenService = new FakeAccessTokenService();
        var casoDeUso = new AutenticarUsuario(new FakeUsuarioRepository(usuario), passwordHasher, accessTokenService);

        var resultado = await casoDeUso.EjecutarAsync(usuario.Email, "otra-clave-ficticia");

        Assert.Null(resultado);
        Assert.Equal((usuario.PasswordHash, "otra-clave-ficticia"), passwordHasher.ArgumentosVerificacion);
        Assert.Null(accessTokenService.ArgumentosEmision);
    }

    [Theory]
    [InlineData("", PasswordDePrueba)]
    [InlineData("ana@example.com", "")]
    public async Task EjecutarAsync_ConDatoObligatorioVacio_LanzaArgumentException(
        string email,
        string password)
    {
        var passwordHasher = new FakePasswordHasher(resultadoVerificacion: true);
        var accessTokenService = new FakeAccessTokenService();
        var casoDeUso = new AutenticarUsuario(new FakeUsuarioRepository(null), passwordHasher, accessTokenService);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            casoDeUso.EjecutarAsync(email, password));

        Assert.Null(passwordHasher.ArgumentosVerificacion);
        Assert.Null(accessTokenService.ArgumentosEmision);
    }

    private sealed class FakeUsuarioRepository(Usuario? usuario) : IUsuarioRepository
    {
        public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult<Usuario?>(usuario?.Id == id ? usuario : null);
        }

        public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken cancellationToken)
        {
            return Task.FromResult(usuario?.Email == email ? usuario : null);
        }

        public Task<bool> ExistePorEmailAsync(string email, CancellationToken cancellationToken)
        {
            return Task.FromResult(usuario?.Email == email);
        }

        public Task<bool> IntentarAgregarAsync(Usuario usuario, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task GuardarCambiosAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePasswordHasher(bool resultadoVerificacion) : IUsuarioPasswordHasher
    {
        public (string PasswordHash, string Password)? ArgumentosVerificacion { get; private set; }

        public string HashPassword(string password)
        {
            throw new NotSupportedException();
        }

        public bool VerifyPassword(string passwordHash, string password)
        {
            ArgumentosVerificacion = (passwordHash, password);
            return resultadoVerificacion;
        }
    }

    private sealed class FakeAccessTokenService : IAccessTokenService
    {
        public DateTimeOffset ExpiresAt { get; } = DateTimeOffset.UtcNow.AddMinutes(5);

        public (Guid UserId, string Name, string Email)? ArgumentosEmision { get; private set; }

        public AccessToken Emitir(Guid userId, string name, string email)
        {
            ArgumentosEmision = (userId, name, email);
            return new AccessToken("token-ficticio-de-prueba", ExpiresAt);
        }
    }
}
