using System.Net;
using System.Net.Http.Json;
using Constia.Web.Habitos;

namespace Constia.Web.Tests;

public sealed class ConsultarEstadisticasHabitoTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 15);
    private static readonly Guid Id = Guid.Parse("cf2b45ca-a606-48b4-9c19-340a11970754");
    private static string Historial => $"/api/habitos/{Id}/historial?desde=2026-10-17&hasta=2026-11-15";

    [Theory]
    [InlineData(7, 3, 0, 70, false)]
    [InlineData(1, 7, 0, 13, false)]
    [InlineData(0, 10, 0, 0, false)]
    [InlineData(0, 0, 0, null, false)]
    [InlineData(3, 1, 1, null, true)]
    [InlineData(1, 0, 1, 100, true)]
    [InlineData(0, 0, 1, null, true)]
    public async Task ResumeOportunidadesSinContarRegistrosFueraDeProgramacion(
        int cumplidos, int pendientes, int desconocidos, int? porcentaje, bool historialParcial)
    {
        using var client = Cliente(ruta => ruta == Historial
            ? Json(Dias(cumplidos, pendientes, desconocidos))
            : throw new InvalidOperationException(ruta));

        var resultado = await Periodo(client);

        Assert.Equal(EstadoProgreso.Disponible, resultado.Estado);
        Assert.Equal(Hoy, resultado.Hoy);
        var periodo = Assert.IsType<PeriodoEstadisticasHabito>(resultado.PeriodoHabito);
        Assert.Equal(30, periodo.Dias.Length);
        Assert.Equal(Hoy, periodo.Dias[^1].Fecha);
        Assert.Equal(cumplidos, periodo.Cumplidos);
        Assert.Equal(cumplidos + pendientes, periodo.Programados);
        Assert.Equal(porcentaje, periodo.Porcentaje);
        Assert.Equal(historialParcial, periodo.HistorialParcial);
    }

    [Fact]
    public async Task Rachas422ConservaElEstadoInactivoConfirmadoPorElDetalle()
    {
        var rutas = new List<string>();
        using var client = Cliente(ruta =>
        {
            rutas.Add(ruta);
            return ruta == $"/api/habitos/{Id}"
                ? Json(new { nombre = "Leer 20 minutos", estado = 1 })
                : new(HttpStatusCode.UnprocessableEntity);
        });
        var (habito, login) = await ConsultarEstadisticasHabito.DetalleAsync(client, Id, default);
        Assert.False(login);
        Assert.NotNull(habito);
        var resultado = await ConsultarEstadisticasHabito.RachasAsync(client, Id, habito, default);

        Assert.Equal(new[] { $"/api/habitos/{Id}", $"/api/habitos/{Id}/rachas" }, rutas);
        Assert.Equal(EstadoProgreso.Insuficiente, resultado.Estado);
        Assert.True(resultado.HabitoInactivo);
        Assert.Null(resultado.Rachas);
    }

    [Fact]
    public async Task ConservaLaMejorRachaYElCorteDelHabitoInactivo()
    {
        using var client = Cliente(ruta => ruta == $"/api/habitos/{Id}/rachas"
            ? Json(new RachasProgreso(Hoy.AddDays(-1), new("no_aplicable", null), new("disponible", 8)))
            : throw new InvalidOperationException(ruta));
        var resultado = await ConsultarEstadisticasHabito.RachasAsync(client, Id, new("Leer", true), default);

        Assert.Equal(EstadoProgreso.Disponible, resultado.Estado);
        Assert.True(resultado.HabitoInactivo);
        Assert.Equal(8, resultado.Rachas?.Mejor.Valor);
        Assert.Equal(Hoy.AddDays(-1), resultado.Rachas?.FechaCorte);
    }

    [Theory]
    [InlineData(503, """{"code":"habit_status_history_not_initialized"}""", EstadoProgreso.SinInicializar)]
    [InlineData(503, """{"code":"otro"}""", EstadoProgreso.Error)]
    [InlineData(503, "<html>", EstadoProgreso.Error)]
    [InlineData(401, "{}", EstadoProgreso.Login)]
    public async Task ClasificaLosErroresDeLasDosSecciones(int status, string cuerpo, EstadoProgreso estado)
    {
        using var client = Cliente(_ => new((HttpStatusCode)status)
        {
            Content = new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/problem+json")
        });
        Assert.Equal(estado, (await Periodo(client)).Estado);
        Assert.Equal(estado, (await ConsultarEstadisticasHabito.RachasAsync(client, Id, new("Leer", false), default)).Estado);
    }

    [Fact]
    public async Task RechazaUnRangoIncompletoYRespetaSesionVencidaAlObtenerFecha()
    {
        using var client = Cliente(_ => Json(Dias(0, 0, 0)[1..]));
        Assert.Equal(EstadoProgreso.Error, (await Periodo(client)).Estado);
        Assert.Equal(EstadoProgreso.Login, (await ConsultarEstadisticasHabito.PeriodoAsync(client, Id,
            Task.FromResult<(DateOnly?, bool)>((null, true)), default)).Estado);
    }

    private static Task<SeccionProgreso> Periodo(HttpClient client) =>
        ConsultarEstadisticasHabito.PeriodoAsync(client, Id, Task.FromResult<(DateOnly?, bool)>((Hoy, false)), default);

    private static DiaHistorial[] Dias(int cumplidos, int pendientes, int desconocidos) =>
        Enumerable.Range(0, 30).Select(i => new DiaHistorial(Hoy.AddDays(i - 29),
            i < cumplidos ? "cumplido" : i < cumplidos + pendientes ? "pendiente" :
            i < cumplidos + pendientes + desconocidos ? "desconocido" : "no_programado",
            i < cumplidos || i >= cumplidos + pendientes)).ToArray();

    private static HttpResponseMessage Json<T>(T valor) => new(HttpStatusCode.OK) { Content = JsonContent.Create(valor) };
    private static HttpClient Cliente(Func<string, HttpResponseMessage> responder) =>
        new(new Api(responder)) { BaseAddress = new Uri("https://api.invalid") };

    private sealed class Api(Func<string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(responder(request.RequestUri!.PathAndQuery));
    }
}
