using System.Security.Cryptography;
using Constia.Domain;
using Constia.Application.Usuarios;

namespace Constia.Tests;
public sealed class PoliticaCorreoTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-21T00:00:00Z");

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    public void Reenvio_Frontera60Segundos(int segundos, bool permitido) =>
        Assert.Equal(permitido,new PoliticaVerificacionCorreo().PermiteEmision([Now],Now.AddSeconds(segundos)));

    [Theory]
    [InlineData(4,true)]
    [InlineData(5,false)]
    public void Reenvio_CuentaEmisionInicialYReemplazadas(int n,bool permitido) =>
        Assert.Equal(permitido,new PoliticaVerificacionCorreo().PermiteEmision(
            Enumerable.Range(0,n).Select(i => Now.AddMinutes(-i-2)).ToArray(),Now));

    [Fact]
    public void Reenvio_VentanaMovilNoSeReiniciaAlCambiarDia()
    {
        var p = new PoliticaVerificacionCorreo();
        var emisiones = Enumerable.Range(0,5).Select(i=>Now.AddMinutes(-i-2)).ToArray();
        Assert.False(p.PermiteEmision(emisiones,Now));
        Assert.True(p.PermiteEmision(emisiones,Now.AddHours(24)));
    }

    [Fact]
    public void Intentos_SeLimitanPorDesafioYSeRecuperanSinConsumir()
    {
        var p = new PoliticaVerificacionCorreo();
        var d = new DesafioVerificacionCorreo(Guid.NewGuid(),"a@example.invalid",new byte[32],Now);
        for(var i=0;i<10;i++) Assert.True(d.RegistrarIntento(Now,p.IntentosPorDesafio,p.VentanaIntentos));
        Assert.False(d.RegistrarIntento(Now.AddMinutes(10).AddTicks(-1),p.IntentosPorDesafio,p.VentanaIntentos));
        Assert.True(d.RegistrarIntento(Now.AddMinutes(10),p.IntentosPorDesafio,p.VentanaIntentos));
        Assert.Null(d.ConsumidoEnUtc);
        Assert.Null(d.InvalidadoEnUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("no-es-un-token")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    public void Token_RechazaFormatoNoCanonico(string valor) =>
        Assert.False(HashTokenVerificacion.IntentarCalcular(valor,out _));

    [Fact]
    public void Token_CalculaHashDe32BytesYComparaSinRecuperarValor()
    {
        var valor = new string('A',43);
        Assert.True(HashTokenVerificacion.IntentarCalcular(valor,out var hash));
        Assert.Equal(32,hash.Length);
        Assert.True(HashTokenVerificacion.Coincide(SHA256.HashData(new byte[32]),hash));
        Assert.False(HashTokenVerificacion.Coincide(new byte[32],hash));
        Assert.False(HashTokenVerificacion.Coincide(new byte[31],hash));
    }

    [Fact]
    public void Desafio_NoAceptaHashCortoNiFechaNoUtc()
    {
        Assert.Throws<ArgumentException>(()=>new DesafioVerificacionCorreo(Guid.NewGuid(),"a@example.invalid",new byte[31],Now));
        Assert.Throws<ArgumentException>(()=>new DesafioVerificacionCorreo(Guid.NewGuid(),"a@example.invalid",new byte[32],Now.ToOffset(TimeSpan.FromHours(1))));
        Assert.Throws<ArgumentException>(()=>new DesafioVerificacionCorreo(Guid.Empty,"a@example.invalid",new byte[32],Now));
    }

    [Fact]
    public void Usuario_VerificadoNoPuedeVolverAPendienteNiCambiarFecha()
    {
        var u = new Usuario("Ana","a@example.invalid","hash","Etc/UTC");
        u.ConfirmarCorreo(Now);
        Assert.Throws<InvalidOperationException>(()=>u.ConfirmarCorreo(Now.AddSeconds(1)));
        Assert.Equal(Now,u.EmailVerificadoEnUtc);
        Assert.False(typeof(Usuario).GetProperty(nameof(Usuario.EstadoVerificacionCorreo))!.SetMethod!.IsPublic);
    }

    [Fact]
    public void Legado_NoRecibeFechaInventada()
    {
        var u = new Usuario("Ana","a@example.invalid","hash","Etc/UTC");
        typeof(Usuario).GetProperty(nameof(Usuario.EstadoVerificacionCorreo))!.SetValue(u,EstadoVerificacionCorreo.LegadoExento);
        Assert.Throws<InvalidOperationException>(()=>u.ConfirmarCorreo(Now));
        Assert.Null(u.EmailVerificadoEnUtc);
        Assert.Equal(EstadoVerificacionCorreo.LegadoExento,u.EstadoVerificacionCorreo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PresupuestoInvalido_CierraPolitica(int limite) =>
        Assert.False(new PoliticaVerificacionCorreo{EmisionesGlobales=limite}.EsValida);
}
