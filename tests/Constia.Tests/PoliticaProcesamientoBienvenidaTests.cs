using Constia.Application.Bienvenida;
using Constia.Application.Correo;
using Constia.Domain;
namespace Constia.Tests;
public sealed class PoliticaProcesamientoBienvenidaTests
{
    [Fact] public void Limites_FijosYConfiguracionCerrada()
    {
        Assert.Equal(3,PoliticaProcesamientoBienvenida.MaxReservas);
        Assert.Equal(20,PoliticaProcesamientoBienvenida.Presupuesto);
        Assert.False(new OpcionesProcesamientoBienvenida().ProcesamientoHabilitado);
        Assert.False(OpcionesProcesamientoBienvenida.DesdeValores("incorrecto",null,null,null,null).EsValida);
    }
    [Theory] [InlineData(false,EstadoEventoBienvenida.FinalSinAceptacion)] [InlineData(true,EstadoEventoBienvenida.InciertoFinal)]
    public void Final_ConservaIncertidumbre(bool incierto,EstadoEventoBienvenida estado) =>
        Assert.Equal(estado,PoliticaProcesamientoBienvenida.Final(incierto));
    [Fact] public void Reintento_RespetaMinimosYRetryAfter()
    {
        Assert.Equal(TimeSpan.FromMinutes(1),PoliticaProcesamientoBienvenida.Espera(1,null));
        Assert.Equal(TimeSpan.FromMinutes(5),PoliticaProcesamientoBienvenida.Espera(2,null));
        Assert.Equal(TimeSpan.FromMinutes(8),PoliticaProcesamientoBienvenida.Espera(2,TimeSpan.FromMinutes(8)));
        Assert.False(PoliticaProcesamientoBienvenida.EsPausa(new(EstadoEntregaCorreo.FalloTemporal,MotivoEntregaCorreo.LimiteVelocidad)));
        Assert.True(PoliticaProcesamientoBienvenida.EsPausa(new(EstadoEntregaCorreo.ProveedorNoDisponible,MotivoEntregaCorreo.Autorizacion)));
    }
}
