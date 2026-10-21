using Constia.Application.Correo;
namespace Constia.Tests;

public sealed class MensajeVerificacionCorreoTests
{
    private static MensajeVerificacionCorreo Crear(Guid? id = null) => new(
        id ?? Guid.Parse("11111111-2222-3333-4444-555555555555"), "persona@example.invalid",
        "correo@example.invalid", "CONSTIA", "asunto privado", "<p>token-privado</p>", "texto privado",
        DateTimeOffset.Parse("2026-10-21T00:00:00Z"), DateTimeOffset.Parse("2026-10-22T00:00:00Z"));

    [Fact]
    public void ConservaFechasYClaveCanonica_SinSettersPublicos()
    {
        var m = Crear();
        Assert.Equal("constia/verificacion/11111111-2222-3333-4444-555555555555", m.ClaveIdempotencia);
        Assert.Equal(57, m.ClaveIdempotencia.Length);
        Assert.Equal(TimeSpan.FromHours(24), m.ExpiraEnUtc - m.EmitidoEnUtc);
        Assert.All(typeof(MensajeVerificacionCorreo).GetProperties(), p => Assert.False(p.SetMethod?.IsPublic == true));
        Assert.Equal("persona@example.invalid", m.Destinatario);
        Assert.Equal("correo@example.invalid", m.Remitente);
    }

    [Fact]
    public void Representacion_NoExponeContenido()
    {
        var text = Crear().ToString();
        foreach (var secreto in new[] { "persona@", "correo@", "asunto privado", "token-privado", "texto privado" })
            Assert.DoesNotContain(secreto, text);
    }

    [Fact]
    public void IdVacio_SeRechazaSinIncluirDatos()
    {
        var error = Assert.Throws<ArgumentException>(() => Crear(Guid.Empty));
        Assert.DoesNotContain("persona@", error.Message);
    }

    [Theory]
    [InlineData("a@example.invalid\r\nBcc:x@example.invalid")]
    [InlineData("dos@example.invalid,tres@example.invalid")]
    [InlineData("Nombre <a@example.invalid>")]
    [InlineData("")]
    public void DestinatarioNoEsUnaDireccionSimple_SeRechaza(string destino) =>
        Assert.Throws<ArgumentException>(() => new MensajeVerificacionCorreo(Guid.NewGuid(), destino,
            "correo@example.invalid", "CONSTIA", "Asunto", "<p>Hola</p>", "Hola",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)));
}
