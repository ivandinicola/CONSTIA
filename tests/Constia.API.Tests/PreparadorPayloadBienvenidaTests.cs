using System.Text;
using System.Text.Json;
using Constia.Application.Bienvenida;
using Constia.Domain;
using Constia.Infrastructure.Bienvenida;
namespace Constia.API.Tests;
public sealed class PreparadorPayloadBienvenidaTests
{
    internal static PerfilResendBienvenida Perfil() => new("cuenta-test","correo@example.invalid");
    [Fact] public void Plantilla_EstableMinimaSinEnlacesNiTokens()
    {
        var p = new PreparadorPayloadBienvenida(Perfil());
        var now=DateTimeOffset.Parse("2026-10-23T00:00:00Z");
        var e=EventoBienvenida.CrearPendiente(Guid.NewGuid(),Guid.NewGuid(),"to@example.invalid",now,now);
        var b=p.Preparar(e); Assert.Equal(b,p.Preparar(e));
        using var d=JsonDocument.Parse(b);var j=d.RootElement;
        Assert.Equal("Te damos la bienvenida a CONSTIA",j.GetProperty("subject").GetString());
        Assert.Contains("Tu correo qued\u00f3 verificado.",j.GetProperty("text").GetString());
        Assert.Contains("Tu constancia, d\u00eda a d\u00eda.",j.GetProperty("text").GetString());
        Assert.DoesNotContain("href",j.GetProperty("html").GetString());
        Assert.DoesNotContain("token",Encoding.UTF8.GetString(b),StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[]{"from","to","subject","html","text"},j.EnumerateObject().Select(x=>x.Name));
    }
    [Fact] public void DestinatarioHistoricoIncompatible_NoSeTrunca()
    {
        var now=DateTimeOffset.UtcNow;var e=EventoBienvenida.CrearPendiente(Guid.NewGuid(),Guid.NewGuid(),new string('a',451),now,now);
        Assert.Throws<ArgumentException>(()=>new PreparadorPayloadBienvenida(Perfil()).Preparar(e));
        Assert.Equal(451,e.Destinatario!.Length);
    }
}
