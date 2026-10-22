using Constia.Application.Correo;
namespace Constia.Tests;
public sealed class PoliticaProcesamientoCorreoTests
{
    [Fact]
    public void RetryAfter_NoSeAcortaParaForzarOtroIntento()
    {
        var p=new PoliticaProcesamientoCorreo();
        var r=new ResultadoEntregaCorreo(EstadoEntregaCorreo.FalloTemporal,MotivoEntregaCorreo.LimiteVelocidad,
            esperaMinima:TimeSpan.FromSeconds(20));
        Assert.Null(p.EsperaReintento(r,TimeSpan.FromSeconds(25),TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(20),p.EsperaReintento(r,TimeSpan.FromSeconds(31),TimeSpan.FromSeconds(10)));
    }
    [Theory]
    [InlineData(MotivoEntregaCorreo.RespuestaMalformada)]
    [InlineData(MotivoEntregaCorreo.RespuestaDesconocida)]
    [InlineData(MotivoEntregaCorreo.RespuestaDemasiadoGrande)]
    public void IncertidumbreSinAutorizacionC1_NoSeReintenta(MotivoEntregaCorreo motivo) =>
        Assert.Null(new PoliticaProcesamientoCorreo().EsperaReintento(new(
            EstadoEntregaCorreo.ResultadoIncierto,motivo),TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(10)));
    [Fact]
    public void FalloPosterior_NoBorraIncertidumbreAnterior()
    {
        var s=new EstadoProcesamientoCorreo();
        s.Registrar(new(EstadoEntregaCorreo.ResultadoIncierto,MotivoEntregaCorreo.Timeout,intentoHttpIniciado:true));
        s.Registrar(new(EstadoEntregaCorreo.RechazadaDefinitivamente,MotivoEntregaCorreo.Autorizacion));
        Assert.Equal(FinTrabajoCorreo.Incierto,s.Final);
        Assert.True(s.HuboIncertidumbre);
        Assert.Equal(2,s.Intentos);
    }
    [Fact]
    public void TimeoutIncompatible_NoHabilitaC2()
    {
        var p=new PoliticaProcesamientoCorreo();
        Assert.False(p.Compatible(TimeSpan.FromSeconds(11)));
        Assert.False(p.Compatible(TimeSpan.Zero));
        Assert.True(p.Compatible(TimeSpan.FromSeconds(10)));
    }
}
