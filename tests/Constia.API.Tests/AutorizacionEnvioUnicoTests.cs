using Constia.Infrastructure.Correo;

namespace Constia.API.Tests;

public sealed class AutorizacionEnvioUnicoTests
{
    [Fact]
    public void Alta_valida_inicia_preparada_y_no_expone_identidad_mutable()
    {
        var ensayo = Guid.NewGuid(); var operacion = Guid.NewGuid();
        var identidad = Enumerable.Repeat((byte)42, 32).ToArray();
        var ahora = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var autorizacion = new AutorizacionEnvioUnico(ensayo, identidad, operacion, ahora);
        identidad[0] = 0;
        Assert.Equal(ensayo, autorizacion.EnsayoId);
        Assert.Equal(operacion, autorizacion.OperacionId);
        Assert.Equal((byte)42, autorizacion.IdentidadDestinatario[0]);
        Assert.Equal(EstadoAutorizacionEnvio.Preparada, autorizacion.Estado);
        Assert.Null(autorizacion.ConsumidaEnUtc);
    }
    [Fact]
    public void Identidades_invalidas_se_rechazan()
    {
        var fecha = DateTimeOffset.UtcNow; var identidad = new byte[32];
        Assert.Throws<ArgumentException>(() => new AutorizacionEnvioUnico(Guid.Empty, identidad, Guid.NewGuid(), fecha));
        Assert.Throws<ArgumentException>(() => new AutorizacionEnvioUnico(Guid.NewGuid(), new byte[31], Guid.NewGuid(), fecha));
        Assert.Throws<ArgumentException>(() => new AutorizacionEnvioUnico(Guid.NewGuid(), identidad, Guid.Empty, fecha));
    }
}
