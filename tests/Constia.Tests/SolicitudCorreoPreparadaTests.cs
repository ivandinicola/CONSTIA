using Constia.Application.Correo;
namespace Constia.Tests;
public sealed class SolicitudCorreoPreparadaTests
{
    [Fact] public void Bytes_DefensivosYTextoRedactado()
    {
        var bytes = new byte[]{1,2,3};
        var s = new SolicitudCorreoPreparada("clave", bytes);
        bytes[0] = 9; var copy = s.CopiarPayload(); copy[0] = 8;
        Assert.Equal(new byte[]{1,2,3},s.CopiarPayload());
        Assert.DoesNotContain("clave",s.ToString());
    }
    [Theory] [InlineData("")] [InlineData("a\nb")]
    public void ClaveInvalida_SeRechaza(string clave) =>
        Assert.Throws<ArgumentException>(() => new SolicitudCorreoPreparada(clave,new byte[]{1}));
}
