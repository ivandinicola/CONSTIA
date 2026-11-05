using Constia.Infrastructure.Correo;

namespace Constia.API.Tests;

public sealed class IdentidadDestinatarioEnsayoTests
{
    [Fact]
    public void Identidad_cambia_con_destinatario_o_clave_y_es_determinista()
    {
        var key = Enumerable.Repeat((byte)1, 32).ToArray(); var otra = Enumerable.Repeat((byte)2, 32).ToArray();
        var a = IdentidadDestinatarioEnsayo.Calcular("first@example.invalid", key);
        Assert.Equal(32, a.Length);
        Assert.Equal(a, IdentidadDestinatarioEnsayo.Calcular("first@example.invalid", key));
        Assert.NotEqual(a, IdentidadDestinatarioEnsayo.Calcular("second@example.invalid", key));
        Assert.NotEqual(a, IdentidadDestinatarioEnsayo.Calcular("first@example.invalid", otra));
    }
    [Fact]
    public void Rechaza_clave_corta_y_destino_invalido()
    {
        Assert.Throws<ArgumentException>(() => IdentidadDestinatarioEnsayo.Calcular("first@example.invalid", new byte[31]));
        Assert.Throws<ArgumentException>(() => IdentidadDestinatarioEnsayo.Calcular("not-an-address", new byte[32]));
    }
}
