using Constia.Application.Usuarios;
using Constia.Application.Temporal;
using Constia.Domain;
using Microsoft.AspNetCore.Identity;

namespace Constia.Tests;

public class RegistrarUsuarioTests
{
    private const string PasswordDePrueba = "clave-de-prueba-no-real";

    [Fact]
    public async Task EjecutarAsync_ConDatosValidos_PersisteUsuarioConHashYDevuelveDatosPublicos()
    {
        var usuarios = new FakeUsuarioRepository();
        var hasher = new IdentityPasswordHasherForTests();
        var casoDeUso = CrearCasoDeUso(usuarios, hasher);

        var resultado = await casoDeUso.EjecutarAsync("Ana", "ana@example.com", PasswordDePrueba, "America/Argentina/Buenos_Aires");

        var usuarioPersistido = Assert.IsType<Usuario>(usuarios.UsuarioPersistido);
        var usuarioRegistrado = Assert.IsType<UsuarioRegistrado>(resultado);

        Assert.Equal(usuarioPersistido.Id, usuarioRegistrado.Id);
        Assert.Equal("Ana", usuarioRegistrado.Nombre);
        Assert.Equal("ana@example.com", usuarioRegistrado.Email);
        Assert.Equal("America/Argentina/Buenos_Aires", usuarioPersistido.TimeZoneId);
        Assert.Equal(1, hasher.CantidadDeHashes);
        Assert.NotEqual(PasswordDePrueba, usuarioPersistido.PasswordHash);
        Assert.True(hasher.VerifyPassword(usuarioPersistido.PasswordHash, PasswordDePrueba));
        Assert.False(hasher.VerifyPassword(usuarioPersistido.PasswordHash, "otra-clave"));
    }

    [Fact]
    public async Task EjecutarAsync_ConEmailExistente_RechazaRegistroAntesDeGenerarHash()
    {
        var usuarios = new FakeUsuarioRepository();
        usuarios.Emails.Add("ana@example.com");
        var hasher = new IdentityPasswordHasherForTests();
        var casoDeUso = CrearCasoDeUso(usuarios, hasher);

        var resultado = await casoDeUso.EjecutarAsync("Ana", "ana@example.com", PasswordDePrueba, "Etc/UTC");

        Assert.Null(resultado);
        Assert.Null(usuarios.UsuarioPersistido);
        Assert.Equal(0, hasher.CantidadDeHashes);
    }

    [Fact]
    public async Task EjecutarAsync_CuandoEmailSeDuplicaDuranteElInsert_RechazaSinPropagarErrorTecnico()
    {
        var usuarios = new FakeUsuarioRepository { RechazarInsertPorEmailDuplicado = true };
        var casoDeUso = CrearCasoDeUso(usuarios, new IdentityPasswordHasherForTests());

        var resultado = await casoDeUso.EjecutarAsync("Ana", "ana@example.com", PasswordDePrueba, "Etc/UTC");

        Assert.Null(resultado);
        Assert.Null(usuarios.UsuarioPersistido);
    }

    [Theory]
    [InlineData("", "ana@example.com", PasswordDePrueba)]
    [InlineData("Ana", " ", PasswordDePrueba)]
    [InlineData("Ana", "ana@example.com", "")]
    public async Task EjecutarAsync_ConDatosObligatoriosInvalidos_LanzaArgumentException(
        string nombre,
        string email,
        string password)
    {
        var usuarios = new FakeUsuarioRepository();
        var hasher = new IdentityPasswordHasherForTests();
        var casoDeUso = CrearCasoDeUso(usuarios, hasher);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            casoDeUso.EjecutarAsync(nombre, email, password, "Etc/UTC"));

        Assert.Null(usuarios.UsuarioPersistido);
        Assert.Equal(0, hasher.CantidadDeHashes);
    }

    [Fact]
    public async Task EjecutarAsync_ConLaMismaPassword_GeneraHashesDiferentesYVerificables()
    {
        var hasher = new IdentityPasswordHasherForTests();
        var primerRepositorio = new FakeUsuarioRepository();
        var segundoRepositorio = new FakeUsuarioRepository();
        var primerUsuario = await CrearCasoDeUso(primerRepositorio, hasher)
            .EjecutarAsync("Ana", "ana@example.com", PasswordDePrueba, "Etc/UTC");
        var segundoUsuario = await CrearCasoDeUso(segundoRepositorio, hasher)
            .EjecutarAsync("Bea", "bea@example.com", PasswordDePrueba, "Etc/UTC");

        Assert.IsType<UsuarioRegistrado>(primerUsuario);
        Assert.IsType<UsuarioRegistrado>(segundoUsuario);
        var primerHash = Assert.IsType<Usuario>(primerRepositorio.UsuarioPersistido).PasswordHash;
        var segundoHash = Assert.IsType<Usuario>(segundoRepositorio.UsuarioPersistido).PasswordHash;
        Assert.NotEqual(
            primerHash,
            segundoHash);
        Assert.True(hasher.VerifyPassword(primerHash, PasswordDePrueba));
        Assert.True(hasher.VerifyPassword(segundoHash, PasswordDePrueba));
    }

    [Fact]
    public async Task EjecutarAsync_ConZonaHorariaInvalida_NoPersisteNiGeneraHash()
    {
        var usuarios = new FakeUsuarioRepository();
        var hasher = new IdentityPasswordHasherForTests();
        var casoDeUso = CrearCasoDeUso(usuarios, hasher);

        await Assert.ThrowsAsync<ZonaHorariaIanaInvalidaException>(() =>
            casoDeUso.EjecutarAsync("Ana", "ana@example.com", PasswordDePrueba, "Eastern Standard Time"));

        Assert.Null(usuarios.UsuarioPersistido);
        Assert.Equal(0, hasher.CantidadDeHashes);
    }

    [Fact]
    public async Task EjecutarAsync_SinZonaHoraria_LanzaZonaHorariaIanaInvalidaException()
    {
        var usuarios = new FakeUsuarioRepository();
        var hasher = new IdentityPasswordHasherForTests();

        await Assert.ThrowsAsync<ZonaHorariaIanaInvalidaException>(() =>
            CrearCasoDeUso(usuarios, hasher).EjecutarAsync("Ana", "ana@example.com", PasswordDePrueba, " "));

        Assert.Null(usuarios.UsuarioPersistido);
        Assert.Equal(0, hasher.CantidadDeHashes);
    }

    [Fact]
    public async Task EjecutarAsync_ConZonaMayorAlLimite_NoPersisteNiGeneraHash()
    {
        var usuarios = new FakeUsuarioRepository();
        var hasher = new IdentityPasswordHasherForTests();

        await Assert.ThrowsAsync<ZonaHorariaIanaInvalidaException>(() =>
            CrearCasoDeUso(usuarios, hasher).EjecutarAsync(
                "Ana",
                "ana@example.com",
                PasswordDePrueba,
                new string('x', ZonaHorariaIana.LongitudMaximaIdentificador + 1)));

        Assert.Null(usuarios.UsuarioPersistido);
        Assert.Equal(0, hasher.CantidadDeHashes);
    }

    private static RegistrarUsuario CrearCasoDeUso(
        IUsuarioRepository usuarios,
        IUsuarioPasswordHasher hasher) => new(usuarios, hasher, new ZonaHorariaIana());

    private sealed class FakeUsuarioRepository : IUsuarioRepository
    {
        public HashSet<string> Emails { get; } = [];

        public Usuario? UsuarioPersistido { get; private set; }

        public bool RechazarInsertPorEmailDuplicado { get; init; }

        public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult<Usuario?>(null);
        }

        public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken cancellationToken)
        {
            return Task.FromResult<Usuario?>(null);
        }

        public Task<bool> ExistePorEmailAsync(string email, CancellationToken cancellationToken)
        {
            return Task.FromResult(Emails.Contains(email));
        }

        public Task<bool> IntentarAgregarAsync(Usuario usuario, CancellationToken cancellationToken)
        {
            if (RechazarInsertPorEmailDuplicado || !Emails.Add(usuario.Email))
            {
                return Task.FromResult(false);
            }

            UsuarioPersistido = usuario;
            return Task.FromResult(true);
        }

        public Task GuardarCambiosAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class IdentityPasswordHasherForTests : IUsuarioPasswordHasher
    {
        private static readonly object UserContext = new();
        private readonly PasswordHasher<object> _passwordHasher = new();

        public int CantidadDeHashes { get; private set; }

        public string HashPassword(string password)
        {
            CantidadDeHashes++;
            return _passwordHasher.HashPassword(UserContext, password);
        }

        public bool VerifyPassword(string passwordHash, string password)
        {
            return _passwordHasher.VerifyHashedPassword(UserContext, passwordHash, password)
                != PasswordVerificationResult.Failed;
        }
    }
}
