using Constia.Application.Temporal;

namespace Constia.Tests;

public sealed class FechaLocalUsuarioTests
{
    [Theory]
    [InlineData("America/Argentina/Buenos_Aires", "2026-01-01T02:30:00+00:00", "2025-12-31")]
    [InlineData("Asia/Tokyo", "2026-01-01T02:30:00+00:00", "2026-01-01")]
    public void ObtenerHoy_ConvierteElInstanteUtcEnLaZonaDelUsuario(
        string timeZoneId,
        string instanteUtc,
        string fechaEsperada)
    {
        var fechaLocal = CrearServicio(DateTimeOffset.Parse(instanteUtc)).ObtenerHoy(timeZoneId);

        Assert.Equal(DateOnly.Parse(fechaEsperada), fechaLocal);
    }

    [Theory]
    [InlineData("2026-01-01T02:59:00+00:00", "2025-12-31")]
    [InlineData("2026-01-01T03:00:00+00:00", "2026-01-01")]
    public void ObtenerHoy_CercaDeMedianocheUtc_RespetaElCambioDeFechaLocal(
        string instanteUtc,
        string fechaEsperada)
    {
        var fechaLocal = CrearServicio(DateTimeOffset.Parse(instanteUtc))
            .ObtenerHoy("America/Argentina/Buenos_Aires");

        Assert.Equal(DateOnly.Parse(fechaEsperada), fechaLocal);
    }

    [Theory]
    [InlineData("2026-03-08T06:59:00+00:00", "2026-03-08")]
    [InlineData("2026-03-08T07:01:00+00:00", "2026-03-08")]
    [InlineData("2026-11-01T05:30:00+00:00", "2026-11-01")]
    public void ObtenerHoy_DuranteTransicionesDst_DevuelveLaFechaLocalCorrecta(
        string instanteUtc,
        string fechaEsperada)
    {
        var fechaLocal = CrearServicio(DateTimeOffset.Parse(instanteUtc))
            .ObtenerHoy("America/New_York");

        Assert.Equal(DateOnly.Parse(fechaEsperada), fechaLocal);
    }

    [Fact]
    public void ObtenerHoy_ConZonaPersistidaInvalida_LanzaErrorDeIntegridadIdentificable()
    {
        var servicio = CrearServicio(DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));

        Assert.Throws<ZonaHorariaPersistidaInvalidaException>(() => servicio.ObtenerHoy("Invalid/Unknown"));
    }

    private static IFechaLocalUsuario CrearServicio(DateTimeOffset instanteUtc)
    {
        return new FechaLocalUsuario(new TimeProviderFijo(instanteUtc), new ZonaHorariaIana());
    }

    private sealed class TimeProviderFijo(DateTimeOffset instanteUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instanteUtc;
    }
}
