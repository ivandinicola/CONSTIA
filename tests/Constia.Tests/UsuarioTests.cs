using System.Reflection;
using Constia.Domain;

namespace Constia.Tests;

public class UsuarioTests
{
    [Fact]
    public void Constructor_WithValidValues_CreatesUsuarioWithUniqueIdentifier()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hashed-value");
        var otroUsuario = new Usuario("Ana", "ana@example.com", "hashed-value");

        Assert.NotEqual(Guid.Empty, usuario.Id);
        Assert.NotEqual(usuario.Id, otroUsuario.Id);
        Assert.Equal("Ana", usuario.Nombre);
        Assert.Equal("ana@example.com", usuario.Email);
        Assert.Equal("hashed-value", usuario.PasswordHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WithEmptyOrWhitespaceName_ThrowsArgumentException(string nombre)
    {
        Assert.Throws<ArgumentException>(() => new Usuario(nombre, "ana@example.com", "hashed-value"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WithEmptyOrWhitespaceEmail_ThrowsArgumentException(string email)
    {
        Assert.Throws<ArgumentException>(() => new Usuario("Ana", email, "hashed-value"));
    }

    [Fact]
    public void Usuario_ExposesPasswordHashAndDoesNotExposePlaintextPassword()
    {
        var propertyNames = typeof(Usuario)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name);

        Assert.Contains(nameof(Usuario.PasswordHash), propertyNames);
        Assert.DoesNotContain("Password", propertyNames);
    }
}
