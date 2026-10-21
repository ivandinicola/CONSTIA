using Constia.Infrastructure.Correo;
namespace Constia.API.Tests;

public sealed class ResendConfiguracionTests
{
    internal static OpcionesResend Validas() => new()
    {
        Enabled = true, ApiKey = "synthetic-key-not-a-secret", FromAddress = "correo@example.invalid",
        PublicOrigin = "https://constia.example"
    };

    [Fact]
    public void AusenteNoHabilitaYDiagnosticosNoExponenValores()
    {
        var o = new OpcionesResend();
        Assert.False(o.Enabled);
        Assert.Equal(10, o.RequestTimeoutSeconds);
        Assert.NotEmpty(ValidadorOpcionesResend.Validar(o));
        Assert.Empty(ValidadorOpcionesResend.Validar(Validas()));
        Assert.DoesNotContain(Validas().ApiKey!, Validas().ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a\r\nBearer:evil")]
    public void ClaveAusenteOInyectadaEsInvalida(string? key) =>
        Assert.Contains("ApiKeyInvalida", ValidadorOpcionesResend.Validar(new()
        { Enabled = true, ApiKey = key, FromAddress = "correo@example.invalid", PublicOrigin = "https://constia.example" }));

    [Theory]
    [InlineData("")]
    [InlineData("mal")]
    [InlineData("a@example.invalid\r\nBcc:x@example.invalid")]
    [InlineData("a@example.invalid,b@example.invalid")]
    public void RemitenteInvalidoSeRechaza(string from) =>
        Assert.Contains("RemitenteInvalido", ValidadorOpcionesResend.Validar(new()
        { ApiKey = "synthetic", FromAddress = from, PublicOrigin = "https://constia.example" }));

    [Theory]
    [InlineData("http://constia.example")]
    [InlineData("https://user:password@constia.example")]
    [InlineData("https://constia.example/path")]
    [InlineData("https://constia.example?x=1")]
    [InlineData("https://constia.example#x")]
    [InlineData("")]
    public void OrigenInseguroSeRechaza(string origin) =>
        Assert.Contains("OrigenInseguro", ValidadorOpcionesResend.Validar(new()
        { ApiKey = "synthetic", FromAddress = "correo@example.invalid", PublicOrigin = origin }));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void TimeoutTieneLimites(int seconds, bool valido) =>
        Assert.Equal(valido, ValidadorOpcionesResend.Validar(new()
        { ApiKey = "synthetic", FromAddress = "correo@example.invalid", PublicOrigin = "https://constia.example",
            RequestTimeoutSeconds = seconds }).Count == 0);
}
