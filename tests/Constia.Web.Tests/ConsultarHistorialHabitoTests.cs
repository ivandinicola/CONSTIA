using System.Net;
using System.Net.Http.Json;
using Constia.Web.Habitos;

namespace Constia.Web.Tests;

public sealed class ConsultarHistorialHabitoTests
{
    [Theory]
    [InlineData(503, """{"code":"habit_status_history_not_initialized"}""", true)]
    [InlineData(503, """{"code":"otro_error"}""", false)]
    [InlineData(503, """{"title":"Unavailable"}""", false)]
    [InlineData(503, "", false)]
    [InlineData(503, "<html>Unavailable</html>", false)]
    [InlineData(503, """{"code":42}""", false)]
    [InlineData(500, """{"code":"habit_status_history_not_initialized"}""", false)]
    public async Task Solo503ConCodigoEsperadoIndicaHistorialSinInicializar(int status, string cuerpo, bool sinInicializar)
    {
        using var cliente = new HttpClient(new Respuestas((HttpStatusCode)status, cuerpo))
        {
            BaseAddress = new Uri("https://api.invalid")
        };

        var resultado = await new ConsultarHistorialHabito().CargarAsync(cliente, Guid.NewGuid(), null, default);

        Assert.Equal(sinInicializar, resultado.SinInicializar);
        Assert.Equal(sinInicializar ? "El historial todavía no está inicializado." : "Volvé a intentar o regresá a tus hábitos.",
            resultado.Error);
        Assert.Null(resultado.Vista);
    }

    private sealed class Respuestas(HttpStatusCode status, string cuerpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var ruta = request.RequestUri!.AbsolutePath;
            return Task.FromResult(ruta.EndsWith("/historial", StringComparison.Ordinal)
                ? new HttpResponseMessage(status) { Content = new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/problem+json") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = ruta.EndsWith("/mi-fecha-local", StringComparison.Ordinal)
                        ? JsonContent.Create(new { fecha = "2026-11-15" })
                        : JsonContent.Create(new { nombre = "Leer 20 minutos" })
                });
        }
    }
}
