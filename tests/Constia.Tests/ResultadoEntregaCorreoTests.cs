using Constia.Application.Correo;
namespace Constia.Tests;

public sealed class ResultadoEntregaCorreoTests
{
    [Theory]
    [InlineData(EstadoEntregaCorreo.AceptadaPorProveedor)]
    [InlineData(EstadoEntregaCorreo.RechazadaDefinitivamente)]
    [InlineData(EstadoEntregaCorreo.FalloTemporal)]
    [InlineData(EstadoEntregaCorreo.ResultadoIncierto)]
    [InlineData(EstadoEntregaCorreo.ProveedorNoDisponible)]
    [InlineData(EstadoEntregaCorreo.CanceladaAntesDelEnvio)]
    public void EstadosSonExplicitosYResultadoInmutable(EstadoEntregaCorreo estado)
    {
        var r = new ResultadoEntregaCorreo(estado, MotivoEntregaCorreo.Ninguno,
            estado == EstadoEntregaCorreo.AceptadaPorProveedor ? "opaque-not-a-uuid" : null,
            intentoHttpIniciado: estado is not (EstadoEntregaCorreo.CanceladaAntesDelEnvio or EstadoEntregaCorreo.ProveedorNoDisponible));
        Assert.Equal(estado, r.Estado);
        Assert.All(typeof(ResultadoEntregaCorreo).GetProperties(), p => Assert.False(p.SetMethod?.IsPublic == true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("id\r\ninjected")]
    public void Aceptacion_RequiereIdSeguro(string? id) =>
        Assert.Throws<ArgumentException>(() => new ResultadoEntregaCorreo(
            EstadoEntregaCorreo.AceptadaPorProveedor, MotivoEntregaCorreo.Ninguno, id, intentoHttpIniciado: true));

    [Fact]
    public void CancelacionConInicioYEsperaNegativa_SeRechazan()
    {
        Assert.Throws<ArgumentException>(() => new ResultadoEntregaCorreo(
            EstadoEntregaCorreo.CanceladaAntesDelEnvio, MotivoEntregaCorreo.Cancelacion, intentoHttpIniciado: true));
        Assert.Throws<ArgumentException>(() => new ResultadoEntregaCorreo(
            EstadoEntregaCorreo.FalloTemporal, MotivoEntregaCorreo.LimiteVelocidad, esperaMinima: TimeSpan.FromSeconds(-1)));
    }
}
