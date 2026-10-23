using Constia.Application.Bienvenida;
namespace Constia.Tests;
public sealed class OpcionesCapturaBienvenidaTests
{
    [Fact] public void Ausencia_DesactivaCaptura()
    { var o=OpcionesCapturaBienvenida.DesdeValores(null,null);Assert.True(o.EsValida);Assert.False(o.Habilitada);Assert.Null(o.Epoca); }
    [Fact] public void HabilitadaConEpocaValida_ConservaValores()
    { var id=Guid.Parse("22222222-2222-2222-2222-222222222222");var o=OpcionesCapturaBienvenida.DesdeValores("true",id.ToString());Assert.True(o.EsValida);Assert.True(o.Habilitada);Assert.Equal(id,o.Epoca); }
    [Fact] public void ValoresInvalidos_NoSeConviertenEnDesactivacionSilenciosa()
    { foreach(var (flag,epoch) in new[]{("quizas",""),("true",""),("true","incorrecto"),("true",Guid.Empty.ToString())})Assert.False(OpcionesCapturaBienvenida.DesdeValores(flag,epoch).EsValida); }
}
