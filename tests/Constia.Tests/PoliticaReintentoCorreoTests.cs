using Constia.Application.Correo;
namespace Constia.Tests;

public sealed class PoliticaReintentoCorreoTests
{
    [Theory]
    [InlineData(EstadoEntregaCorreo.AceptadaPorProveedor, false)]
    [InlineData(EstadoEntregaCorreo.RechazadaDefinitivamente, false)]
    [InlineData(EstadoEntregaCorreo.ProveedorNoDisponible, false)]
    [InlineData(EstadoEntregaCorreo.CanceladaAntesDelEnvio, false)]
    [InlineData(EstadoEntregaCorreo.FalloTemporal, true)]
    [InlineData(EstadoEntregaCorreo.ResultadoIncierto, true)]
    public void ClasificaSinEjecutarReintentos(EstadoEntregaCorreo estado, bool candidato)
    {
        var r = new ResultadoEntregaCorreo(estado, MotivoEntregaCorreo.Ninguno,
            estado == EstadoEntregaCorreo.AceptadaPorProveedor ? "id" : null,
            intentoHttpIniciado: estado == EstadoEntregaCorreo.AceptadaPorProveedor);
        Assert.Equal(candidato, PoliticaReintentoCorreo.EsCandidatoMismaSolicitud(r));
    }

    [Fact]
    public void DesconocidoNoAbreReintentoYEsperaNoSeAcorta()
    {
        var r = new ResultadoEntregaCorreo(EstadoEntregaCorreo.ResultadoIncierto,
            MotivoEntregaCorreo.RespuestaDesconocida, esperaMinima: TimeSpan.FromHours(2), intentoHttpIniciado: true);
        Assert.False(PoliticaReintentoCorreo.EsCandidatoMismaSolicitud(r));
        Assert.Equal(TimeSpan.FromHours(2), r.EsperaMinima);
    }
}
