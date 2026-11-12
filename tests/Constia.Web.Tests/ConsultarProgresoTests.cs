using System.Net;
using System.Net.Http.Json;
using Constia.Web.Habitos;

namespace Constia.Web.Tests;

public sealed class ConsultarProgresoTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 11);
    private const string FechaLocal = "/api/usuarios/mi-fecha-local";
    private const string Calendario = "/api/habitos/calendario?desde=2026-10-13&hasta=2026-11-11";
    private const string Rachas = "/api/habitos/rachas";

    [Theory]
    [InlineData(30, 8, 27)]
    [InlineData(8, 1, 13)]
    [InlineData(8, 0, 0)]
    [InlineData(0, 0, null)]
    public async Task ResumeOportunidadesDelRangoCivilCompleto(int programados, int cumplidos, int? porcentaje)
    {
        var dias = Dias(programados, cumplidos);
        var handler = new Api((ruta, _) => ruta switch
        {
            FechaLocal => Json(new { fecha = Hoy }),
            Calendario => Json(dias),
            _ => throw new InvalidOperationException(ruta)
        });
        using var client = Cliente(handler);

        var resultado = await Periodo(client);

        Assert.Equal(EstadoProgreso.Disponible, resultado.Estado);
        Assert.Equal(Hoy, resultado.Hoy);
        var periodo = Assert.IsType<PeriodoProgreso>(resultado.Periodo);
        Assert.Equal(programados, periodo.Programados);
        Assert.Equal(cumplidos, periodo.Cumplidos);
        Assert.Equal(porcentaje, periodo.Porcentaje);
        Assert.Equal(30, periodo.Dias.Length);
        Assert.Equal(Hoy, periodo.Dias[^1].Fecha);
        Assert.Equal(Hoy.AddDays(-29), periodo.Dias[0].Fecha);
        Assert.Equal(0, periodo.Dias[0].Programados);
        Assert.Equal(new[] { FechaLocal, Calendario }, handler.Rutas);
    }

    [Fact]
    public async Task LaRachaCeroYLaMejorNoDisponibleSeConservanPorSeparado()
    {
        using var client = Cliente(new Api((_, _) => Json(new
        {
            fechaCorte = Hoy,
            actual = new { estado = "disponible", valor = 0 },
            mejor = new { estado = "no_disponible", valor = (int?)null }
        })));

        var resultado = await ConsultarProgreso.RachasAsync(client, default);

        Assert.Equal(EstadoProgreso.Disponible, resultado.Estado);
        Assert.Equal(new MetricaProgreso("disponible", 0), resultado.Rachas?.Actual);
        Assert.Equal(new MetricaProgreso("no_disponible", null), resultado.Rachas?.Mejor);
    }

    [Theory]
    [InlineData(422, "{}", EstadoProgreso.Insuficiente)]
    [InlineData(503, """{"code":"habit_status_history_not_initialized"}""", EstadoProgreso.SinInicializar)]
    [InlineData(503, """{"code":"otro"}""", EstadoProgreso.Error)]
    [InlineData(503, "", EstadoProgreso.Error)]
    [InlineData(503, "<html>", EstadoProgreso.Error)]
    [InlineData(500, """{"code":"habit_status_history_not_initialized"}""", EstadoProgreso.Error)]
    [InlineData(401, "{}", EstadoProgreso.Login)]
    public async Task ClasificaLosErroresEnCadaSeccion(int status, string cuerpo, EstadoProgreso esperado)
    {
        using var client = Cliente(new Api((ruta, _) => ruta == FechaLocal
            ? Json(new { fecha = Hoy })
            : new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/problem+json")
            }));

        Assert.Equal(esperado, (await Periodo(client)).Estado);
        Assert.Equal(esperado, (await ConsultarProgreso.RachasAsync(client, default)).Estado);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReintentoRepiteSoloLaConsultaDeLaSeccionQueFallo(bool reintentarRachas)
    {
        var handler = new Api((ruta, intento) =>
        {
            if (ruta == FechaLocal)
                return Json(new { fecha = Hoy });

            if (intento == 1 && ruta == (reintentarRachas ? Rachas : Calendario))
                return new(HttpStatusCode.ServiceUnavailable);

            return ruta == Calendario ? Json(Dias(30, 8)) : Json(new RachasProgreso(
                Hoy, new("disponible", 3), new("disponible", 9)));
        });
        using var client = Cliente(handler);

        var periodo = await Periodo(client);
        var rachas = await ConsultarProgreso.RachasAsync(client, default);
        Assert.Equal(EstadoProgreso.Error, (reintentarRachas ? rachas : periodo).Estado);
        Assert.Equal(EstadoProgreso.Disponible, (reintentarRachas ? periodo : rachas).Estado);

        var reintento = await (reintentarRachas ? ConsultarProgreso.RachasAsync(client, default) : Periodo(client));

        Assert.Equal(EstadoProgreso.Disponible, reintento.Estado);
        Assert.Equal(reintentarRachas
            ? new[] { FechaLocal, Calendario, Rachas, Rachas }
            : new[] { FechaLocal, Calendario, Rachas, FechaLocal, Calendario }, handler.Rutas);
    }

    [Fact]
    public async Task UnRangoIncompletoNoSeMuestraComoCero()
    {
        using var client = Cliente(new Api((ruta, _) => ruta == FechaLocal
            ? Json(new { fecha = Hoy })
            : Json(Dias(0, 0)[1..])));

        Assert.Equal(EstadoProgreso.Error, (await Periodo(client)).Estado);
    }

    [Fact]
    public async Task UnaSesionVencidaAlConsultarLaFechaNoConsultaCalendario()
    {
        var handler = new Api((_, _) => new(HttpStatusCode.Unauthorized));
        using var client = Cliente(handler);

        Assert.Equal(EstadoProgreso.Login, (await Periodo(client)).Estado);
        Assert.Equal(new[] { FechaLocal }, handler.Rutas);
    }

    private static Task<SeccionProgreso> Periodo(HttpClient client) =>
        ConsultarProgreso.PeriodoAsync(client, ConsultarProgreso.FechaLocalAsync(client, default), default);

    private static DiaResumen[] Dias(int programados, int cumplidos) =>
        Enumerable.Range(0, 30)
            .Select(i => i switch
            {
                28 => new DiaResumen(Hoy.AddDays(-1), Math.Max(0, programados - 1), Math.Max(0, cumplidos - 1)),
                29 => new DiaResumen(Hoy, programados > 0 ? 1 : 0, cumplidos > 0 ? 1 : 0),
                _ => new DiaResumen(Hoy.AddDays(i - 29), 0, 0)
            })
            .ToArray();

    private static HttpResponseMessage Json<T>(T valor) => new(HttpStatusCode.OK) { Content = JsonContent.Create(valor) };
    private static HttpClient Cliente(Api handler) => new(handler) { BaseAddress = new Uri("https://api.invalid") };

    private sealed class Api(Func<string, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> Rutas { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var ruta = request.RequestUri!.PathAndQuery;
            Rutas.Add(ruta);
            return Task.FromResult(responder(ruta, Rutas.Count(x => x == ruta)));
        }
    }
}
