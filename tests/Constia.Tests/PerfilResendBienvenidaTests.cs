using Constia.Application.Bienvenida;
namespace Constia.Tests;
public sealed class PerfilResendBienvenidaTests
{
    [Fact] public void Perfil_EstableSinSecretosYConCambiosDetectables()
    {
        var a = new PerfilResendBienvenida("cuenta-piloto", "correo@example.invalid");
        Assert.Equal(64, a.Huella.Length);
        Assert.Equal(a.Huella, new PerfilResendBienvenida("cuenta-piloto", "correo@example.invalid").Huella);
        Assert.NotEqual(a.Huella, new PerfilResendBienvenida("otra-cuenta", "correo@example.invalid").Huella);
        Assert.NotEqual(a.Huella, new PerfilResendBienvenida("cuenta-piloto", "otro@example.invalid").Huella);
        Assert.DoesNotContain("correo", a.ToString()); Assert.DoesNotContain("cuenta", a.ToString());
    }
    [Theory] [InlineData("")] [InlineData("cuenta\n")] [InlineData("clave con espacios")]
    public void CuentaInvalida_SeRechaza(string cuenta) =>
        Assert.Throws<ArgumentException>(() => new PerfilResendBienvenida(cuenta, "correo@example.invalid"));
}
