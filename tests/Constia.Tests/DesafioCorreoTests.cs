using System.Reflection;
using Constia.Domain;
using Constia.Application.Usuarios;
namespace Constia.Tests;
public sealed class DesafioCorreoTests
{
    [Fact]
    public void Desafio_VenceExactamenteALas24Horas()
    {
        var type = typeof(Usuario).Assembly.GetType("Constia.Domain.DesafioVerificacionCorreo");
        Assert.NotNull(type);
        var now = DateTimeOffset.Parse("2026-10-21T00:00:00Z");
        var d = Activator.CreateInstance(type, [Guid.NewGuid(), "a@example.invalid", new byte[32], now])!;
        var vigente = type.GetMethod("EstaVigente")!;
        Assert.Equal(true, vigente.Invoke(d, [now.AddHours(24).AddTicks(-1)]));
        Assert.Equal(false, vigente.Invoke(d, [now.AddHours(24)]));
        type.GetMethod("Consumir")!.Invoke(d, [now.AddMinutes(1)]);
        Assert.Equal(false, vigente.Invoke(d, [now.AddMinutes(2)]));
    }

    [Fact]
    public void Desafio_Invalidado_NoPuedeConsumirse()
    {
        var type = typeof(Usuario).Assembly.GetType("Constia.Domain.DesafioVerificacionCorreo");
        Assert.NotNull(type);
        var now = DateTimeOffset.Parse("2026-10-21T00:00:00Z");
        var d = Activator.CreateInstance(type, [Guid.NewGuid(), "a@example.invalid", new byte[32], now])!;
        type.GetMethod("Invalidar")!.Invoke(d, [now]);
        var ex = Assert.Throws<TargetInvocationException>(() => type.GetMethod("Consumir")!.Invoke(d, [now]));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }
}
