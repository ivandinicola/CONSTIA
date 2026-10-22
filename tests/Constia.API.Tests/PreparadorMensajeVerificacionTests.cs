using System.Net;
using Constia.Infrastructure.Correo;
namespace Constia.API.Tests;

public sealed class PreparadorMensajeVerificacionTests
{
    [Fact]
    public void HtmlYTexto_ConservanEnlaceFechasYEscapanNombre()
    {
        var p = new PreparadorMensajeVerificacion(ResendConfiguracionTests.Validas());
        var inicio = DateTimeOffset.Parse("2026-10-21T00:00:00Z");
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var token = new string('A', 43); // Token canónico sintético, nunca una emisión real.
        var m = p.Preparar(id, "destino@example.invalid", "<b>Ana & \"A\"</b>", token, inicio, inicio.AddHours(24));
        var url = $"https://constia.example/confirmar-correo#desafioId={id:D}&token={token}";
        Assert.Contains(url, m.TextoPlano);
        Assert.Contains(WebUtility.HtmlEncode(url), m.Html);
        Assert.Contains("&lt;b&gt;", m.Html);
        Assert.DoesNotContain("<b>Ana", m.Html);
        Assert.Equal(inicio, m.EmitidoEnUtc);
        Assert.Equal(inicio.AddHours(24), m.ExpiraEnUtc);
        Assert.Contains("contraseña", m.TextoPlano);
        Assert.Contains("24 horas", m.TextoPlano);
        foreach(var item in new[] { "<script", "<form", "<img", "tracking", "onclick=" })
            Assert.DoesNotContain(item, m.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, m.ToString());
    }

    [Theory]
    [InlineData("Nombre\r\nPrivado")]
    [InlineData("")]
    public void NombreHistoricoIncompatible_UsaSaludoNeutro(string nombre)
    {
        var p=new PreparadorMensajeVerificacion(ResendConfiguracionTests.Validas());
        var ahora=DateTimeOffset.Parse("2026-10-22T00:00:00Z");
        var m=p.Preparar(Guid.NewGuid(),"a@example.invalid",nombre,new string('A',43),ahora,ahora.AddHours(24));
        Assert.Contains("Hola.",m.Html);Assert.Contains("Hola.",m.TextoPlano);
        Assert.DoesNotContain("Privado",m.Html);Assert.DoesNotContain("Privado",m.TextoPlano);
    }
    [Fact]
    public void DatosInvalidos_NoFiltranEntrada()
    {
        var p = new PreparadorMensajeVerificacion(ResendConfiguracionTests.Validas());
        var error = Assert.Throws<ArgumentException>(() => p.Preparar(Guid.NewGuid(), "destino@example.invalid",
            "NOMBRE-PRIVADO", "TOKEN-PRIVADO", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)));
        Assert.DoesNotContain("PRIVADO", error.Message);
    }
}
