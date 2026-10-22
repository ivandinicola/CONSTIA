using Constia.Application.Correo;
namespace Constia.Tests;
public sealed class DatosVerificacionCorreoTests
{
    [Theory]
    [InlineData("ana@example.invalid",true)]
    [InlineData("Ana <ana@example.invalid>",false)]
    [InlineData("ana@example.invalid ",false)]
    [InlineData("a@example.invalid\r\nb@example.invalid",false)]
    [InlineData("",false)]
    public void Destinatario_NoNormalizaNiAdmiteListas(string email,bool valido) =>
        Assert.Equal(valido,DatosVerificacionCorreo.DireccionSimple(email));
    [Fact]
    public void Limites_NoAceptanContenidoSinCota()
    {
        Assert.True(DatosVerificacionCorreo.NombreValido(new string('a',200)));
        Assert.False(DatosVerificacionCorreo.NombreValido(new string('a',201)));
        Assert.False(DatosVerificacionCorreo.DireccionSimple(new string('a',250)+"@example.invalid"));
        Assert.False(DatosVerificacionCorreo.NombreValido("Ana\nPrivado"));
        Assert.Throws<DatosCorreoInvalidosException>(()=>DatosVerificacionCorreo.ValidarAlta("Ana","a@x.invalid "));
    }
}
