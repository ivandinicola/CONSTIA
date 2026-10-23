using Constia.Domain;
namespace Constia.Tests;
public sealed class EventoBienvenidaTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Epoch = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-22T10:00:00Z");
    [Fact] public void CrearPendiente_ConservaIdentidadEpocaYFechasUtc()
    {
        var e = EventoBienvenida.CrearPendiente(User, Epoch, "viejo@example.invalid", Now.AddDays(-1), Now);
        Assert.NotEqual(Guid.Empty,e.Id); Assert.Equal(User,e.UsuarioId); Assert.Equal(Epoch,e.EpocaCaptura);
        Assert.Equal(Now.AddDays(-1),e.ConfirmadoEnUtc); Assert.Equal(Now.AddHours(24),e.ExpiraEnUtc);
        Assert.Equal(EstadoEventoBienvenida.Pendiente,e.Estado); Assert.Equal(0,e.Reservas);
        Assert.Null(e.PayloadUtf8); Assert.Null(e.LeaseId); Assert.Null(e.IdProveedor); Assert.False(e.HuboIncertidumbre);
    }
    [Fact] public void IdentidadesVacias_SeRechazan()
    {
        Assert.Throws<ArgumentException>(()=>EventoBienvenida.CrearPendiente(Guid.Empty,Epoch,"x",Now,Now));
        Assert.Throws<ArgumentException>(()=>EventoBienvenida.CrearPendiente(User,Guid.Empty,"x",Now,Now));
    }
    [Fact] public void FechasNoUtc_SeRechazan()
    {
        Assert.Throws<ArgumentException>(()=>EventoBienvenida.CrearPendiente(User,Epoch,"x",Now.ToOffset(TimeSpan.FromHours(1)),Now));
        Assert.Throws<ArgumentException>(()=>EventoBienvenida.CrearPendiente(User,Epoch,"x",Now,Now.ToOffset(TimeSpan.FromHours(-3))));
    }
    [Fact] public void DestinatarioHistorico_SeConservaSinNormalizarNiRevalidarAlta()
    {
        var address="  Nombre <"+new string('x',600)+"@example.invalid>  ";
        var historical = new Usuario("Histórico", address, "hash-ficticio", "Etc/UTC");
        Assert.True(historical.Email.Length > 450);
        Assert.Equal(address, historical.Email);
        Assert.Equal(address,EventoBienvenida.CrearPendiente(historical.Id,Epoch,historical.Email,Now,Now).Destinatario);
    }
    [Fact] public void ClaveIdempotencia_EstableYSeparadaDeVerificacion()
    {
        var e=EventoBienvenida.CrearPendiente(User,Epoch,"x",Now,Now);
        Assert.Equal("constia/bienvenida/"+e.Id.ToString("D"),e.ClaveIdempotencia);
        Assert.DoesNotContain("verificacion",e.ClaveIdempotencia);
    }
    [Fact] public void ToString_NoExponeDatosPersonales()
    {
        var e=EventoBienvenida.CrearPendiente(User,Epoch,"PRIVADO@example.invalid",Now,Now);
        Assert.DoesNotContain("PRIVADO",e.ToString()); Assert.DoesNotContain("@",e.ToString());
    }
}
