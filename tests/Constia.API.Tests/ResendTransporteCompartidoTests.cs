using System.Net;
using System.Text;
using Constia.Application.Correo;
using Constia.Infrastructure.Correo;
namespace Constia.API.Tests;
public sealed class ResendTransporteCompartidoTests
{
    [Fact]
    public async Task C1_Caracterizacion_BytesHeadersYOrdenExactos()
    {
        var payload = new List<byte[]>();
        var h = new ResendHttpClientTests.Handler(async (r, ct) =>
        {
            Assert.Equal("https://api.resend.com/emails", r.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", r.Headers.Authorization!.Scheme);
            Assert.Equal("synthetic-key-not-a-secret", r.Headers.Authorization.Parameter);
            Assert.Equal("constia/verificacion/11111111-2222-3333-4444-555555555555", Assert.Single(r.Headers.GetValues("Idempotency-Key")));
            Assert.Equal("application/json; charset=utf-8", r.Content!.Headers.ContentType!.ToString());
            Assert.Equal("application/json", Assert.Single(r.Headers.Accept).MediaType);
            payload.Add(await r.Content.ReadAsByteArrayAsync(ct));
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"opaque-id\"}") };
        });
        var m = new MensajeVerificacionCorreo(Guid.Parse("11111111-2222-3333-4444-555555555555"),
            "to@example.invalid", "correo@example.invalid", "CONSTIA", "Asunto", "<p>Hola</p>", "Hola",
            DateTimeOffset.Parse("2026-10-23T00:00:00Z"), DateTimeOffset.Parse("2026-10-24T00:00:00Z"));
        var client = new ResendEmailClient(new HttpClient(h), ResendConfiguracionTests.Validas(), TimeProvider.System);
        await client.EnviarAsync(m, default);
        await client.EnviarAsync(m, default);
        const string expected = "{\"from\":\"CONSTIA \u003Ccorreo@example.invalid\u003E\",\"to\":[\"to@example.invalid\"],\"subject\":\"Asunto\",\"html\":\"\u003Cp\u003EHola\u003C/p\u003E\",\"text\":\"Hola\"}";
        // Literal golden of the integrated System.Text.Json default encoder.
        var golden = expected.Replace("<", "\\u003C").Replace(">", "\\u003E");
        Assert.Equal(Encoding.UTF8.GetBytes(golden), payload[0]);
        Assert.Equal(payload[0], payload[1]); Assert.Equal(2, h.Calls);
    }
}
