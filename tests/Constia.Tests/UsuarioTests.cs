using System.Reflection;
using Constia.Domain;

namespace Constia.Tests;

public class UsuarioTests
{
    [Fact]
    public void Constructor_WithValidValues_CreatesUsuarioWithUniqueIdentifier()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hashed-value", "Etc/UTC");
        var otroUsuario = new Usuario("Ana", "ana@example.com", "hashed-value", "Etc/UTC");

        Assert.NotEqual(Guid.Empty, usuario.Id);
        Assert.NotEqual(usuario.Id, otroUsuario.Id);
        Assert.Equal("Ana", usuario.Nombre);
        Assert.Equal("ana@example.com", usuario.Email);
        Assert.Equal("hashed-value", usuario.PasswordHash);
        Assert.Equal("Etc/UTC", usuario.TimeZoneId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WithEmptyOrWhitespaceName_ThrowsArgumentException(string nombre)
    {
        Assert.Throws<ArgumentException>(() => new Usuario(nombre, "ana@example.com", "hashed-value", "Etc/UTC"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WithEmptyOrWhitespaceEmail_ThrowsArgumentException(string email)
    {
        Assert.Throws<ArgumentException>(() => new Usuario("Ana", email, "hashed-value", "Etc/UTC"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WithEmptyTimeZoneId_ThrowsArgumentException(string timeZoneId)
    {
        Assert.Throws<ArgumentException>(() => new Usuario("Ana", "ana@example.com", "hashed-value", timeZoneId));
    }

    [Fact]
    public void CambiarZonaHoraria_WithNonEmptyId_ChangesOnlyTimeZoneId()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hashed-value", "Etc/UTC");

        usuario.CambiarZonaHoraria("America/Argentina/Buenos_Aires");

        Assert.Equal("America/Argentina/Buenos_Aires", usuario.TimeZoneId);
        Assert.Equal("Ana", usuario.Nombre);
        Assert.Equal("ana@example.com", usuario.Email);
        Assert.Equal("hashed-value", usuario.PasswordHash);
    }

    [Fact]
    public void CambiarZonaHoraria_WithEmptyId_ThrowsArgumentException()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hashed-value", "Etc/UTC");

        Assert.Throws<ArgumentException>(() => usuario.CambiarZonaHoraria(" "));
        Assert.Equal("Etc/UTC", usuario.TimeZoneId);
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
