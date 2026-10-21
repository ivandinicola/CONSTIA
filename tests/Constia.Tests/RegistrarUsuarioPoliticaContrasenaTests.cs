using Constia.Application.Temporal;
using Constia.Application.Usuarios;
using Constia.Domain;

namespace Constia.Tests;

public sealed class RegistrarUsuarioPoliticaContrasenaTests
{
    public static TheoryData<string> ContrasenasInvalidas => new()
    {
        "ABCD1.@", // Siete valores escalares.
        new string('A', 127) + "1.", // 129 valores escalares.
        "abcdef1.", // Sin mayuscula.
        "ABCDEFG.", // Sin digito.
        "ABCDEFG1", // Sin puntuacion ni simbolo.
        "ABCDE1  ", // Los espacios no son simbolos.
        "ABCDE1\u00A0\u2003", // Tampoco los espacios Unicode.
        "ABCD1.\U0001F600", // Siete escalares, ocho unidades UTF-16.
        new string('A', 126) + "1.\U0001F600", // 129 escalares.
        "\u01C5abcde1.", // Titlecase no es UppercaseLetter.
        "ABCDE\u00B2.@", // Un superindice no es DecimalDigitNumber.
        "ABCDE1a\u0301" // Una marca combinante no es puntuacion ni simbolo.
    };

    public static TheoryData<string> ContrasenasValidas => new()
    {
        "ABCDE1.@", // Ocho escalares, sin minusculas.
        new string('A', 126) + "1.", // 128 escalares.
        "ABCDEFG1.",
        "ABCDEFG1,",
        "ABCDEFG1@",
        " ABCD1.@ ", // No recortar espacios.
        "\u00C9BCDE1.@", // Mayuscula Unicode.
        "\U00010400bcdef1.", // Mayuscula fuera del plano basico.
        "ABCDE\u0661.@", // Digito decimal arabe.
        "ABCDEF1\u20AC", // Simbolo monetario.
        "ABCDEF1\U0001F600", // Un emoji puede ser un simbolo.
        "ABCDE\u03011.", // No normalizar: ocho escalares descompuestos.
        new string('A', 125) + "1.\U0001F600" // 128 escalares, 129 unidades UTF-16.
    };

    [Theory]
    [MemberData(nameof(ContrasenasInvalidas))]
    public Task Registro_ConContrasenaInvalida_RechazaSinHashNiPersistencia(string password) =>
        VerificarRechazoAsync(password);

    // Crear los sustitutos aqui evita que la serializacion del runner los reemplace.
    [Theory]
    [InlineData(0xD800, false)]
    [InlineData(0xDC00, false)]
    [InlineData(0xD800, true)]
    public Task Registro_ConUtf16Malformado_RechazaSinHashNiPersistencia(int sustituto, bool agregarSufijo) =>
        VerificarRechazoAsync("ABCDE1." + (char)sustituto + (agregarSufijo ? "x" : ""));

    private static async Task VerificarRechazoAsync(string password)
    {
        var usuarios = new RepositorioObservado();
        var hasher = new HasherObservado();
        var registrar = new RegistrarUsuario(usuarios, hasher, new ZonaHorariaIana(), new RegistroCorreoFalso(usuarios));

        var error = await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            registrar.EjecutarAsync("Ana", "ana@example.invalid", password, "Etc/UTC"));

        Assert.Equal("password", error.ParamName);
        Assert.DoesNotContain(password, error.Message);
        Assert.Equal(0, hasher.CantidadDeHashes);
        Assert.Equal(0, usuarios.IntentosDeInsercion);
        Assert.Null(usuarios.Persistido);
    }

    [Theory]
    [MemberData(nameof(ContrasenasValidas))]
    public async Task Registro_ConContrasenaValida_HasheaElValorExactoSinTransformarlo(string password)
    {
        var usuarios = new RepositorioObservado();
        var hasher = new HasherObservado();
        var registrar = new RegistrarUsuario(usuarios, hasher, new ZonaHorariaIana(), new RegistroCorreoFalso(usuarios));

        var resultado = await registrar.EjecutarAsync("Ana", "ana@example.invalid", password, "Etc/UTC");

        Assert.NotNull(resultado);
        Assert.Equal(1, hasher.CantidadDeHashes);
        Assert.Equal(password, hasher.PasswordRecibida);
        Assert.Equal(1, usuarios.IntentosDeInsercion);
        var persistido = Assert.IsType<Usuario>(usuarios.Persistido);
        Assert.Equal("hash-ficticio-no-recuperable", persistido.PasswordHash);
        Assert.NotEqual(password, persistido.PasswordHash);
        Assert.Equal(persistido.Id, resultado.Id);
    }

    private sealed class HasherObservado : IUsuarioPasswordHasher
    {
        public int CantidadDeHashes { get; private set; }
        public string? PasswordRecibida { get; private set; }

        public string HashPassword(string password)
        {
            CantidadDeHashes++;
            PasswordRecibida = password;
            return "hash-ficticio-no-recuperable";
        }

        public bool VerifyPassword(string passwordHash, string password) =>
            throw new NotSupportedException();
    }

    private sealed class RepositorioObservado : IUsuarioRepository
    {
        public int IntentosDeInsercion { get; private set; }
        public Usuario? Persistido { get; private set; }

        public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Usuario?>(null);
        public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult<Usuario?>(null);
        public Task<bool> ExistePorEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<bool> IntentarAgregarAsync(Usuario usuario, CancellationToken cancellationToken)
        {
            IntentosDeInsercion++;
            Persistido = usuario;
            return Task.FromResult(true);
        }
        public Task GuardarCambiosAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
