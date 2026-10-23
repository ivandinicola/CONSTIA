using Constia.Application.Bienvenida;
using Constia.Domain;
namespace Constia.Tests;
public sealed class PoliticaRetencionBienvenidaTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-22T10:00:00Z");
    [Fact] public void RespetaUltimoLeaseMasDiezMinutos()
    {
        Assert.False(PoliticaRetencionBienvenida.PuedeDepurar(EstadoEventoBienvenida.FinalSinAceptacion,Now.AddMinutes(-20),Now.AddMinutes(-10).AddTicks(1),Now));
        Assert.True(PoliticaRetencionBienvenida.PuedeDepurar(EstadoEventoBienvenida.FinalSinAceptacion,Now.AddMinutes(-20),Now.AddMinutes(-10),Now));
    }
    [Fact] public void NoDepuraEstadosActivos()
    {
        foreach(var s in new[]{EstadoEventoBienvenida.Pendiente,EstadoEventoBienvenida.Tomado,EstadoEventoBienvenida.ReservadoSinResultado,EstadoEventoBienvenida.EsperandoReintento,EstadoEventoBienvenida.Pausado})
            Assert.False(PoliticaRetencionBienvenida.PuedeDepurar(s,Now.AddDays(-40),null,Now));
        Assert.False(PoliticaRetencionBienvenida.PuedeDepurar(EstadoEventoBienvenida.AceptadoProveedor,null,null,Now));
    }
    [Fact] public void DetalleSeConservaTreintaDiasYRespetaVentanaDeCuota()
    {
        Assert.False(PoliticaRetencionBienvenida.PuedeEliminarDetalle(Now.AddDays(-30).AddTicks(1),Now.AddDays(-31),Now));
        Assert.True(PoliticaRetencionBienvenida.PuedeEliminarDetalle(Now.AddDays(-30),Now.AddDays(-31),Now));
        Assert.False(PoliticaRetencionBienvenida.PuedeEliminarDetalle(Now.AddDays(-40),Now.AddHours(-23),Now));
    }
}
