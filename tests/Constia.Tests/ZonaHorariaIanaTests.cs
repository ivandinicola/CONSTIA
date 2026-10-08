using Constia.Application.Temporal;

namespace Constia.Tests;

public sealed class ZonaHorariaIanaTests
{
    private readonly ZonaHorariaIana _zonasHorarias = new();

    [Theory]
    [InlineData("Etc/UTC")]
    [InlineData("America/Argentina/Buenos_Aires")]
    public void EsValida_ConIdentificadorIanaReconocido_DevuelveTrue(string timeZoneId)
    {
        Assert.True(_zonasHorarias.EsValida(timeZoneId));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Invalid/Unknown")]
    [InlineData("Eastern Standard Time")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void EsValida_ConIdentificadorNoIana_DevuelveFalse(string? timeZoneId)
    {
        Assert.False(_zonasHorarias.EsValida(timeZoneId));
    }
}
