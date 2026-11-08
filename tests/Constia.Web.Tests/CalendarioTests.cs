using System.Net;
using System.Net.Http.Json;
using Constia.Web.Habitos;
namespace Constia.Web.Tests;
public sealed class CalendarioTests
{
    [Theory]
    [InlineData("2021-02-01","2021-02-01","2021-02-28")]
    [InlineData("2024-02-01","2024-01-29","2024-03-03")]
    [InlineData("2026-11-01","2026-10-26","2026-12-06")]
    [InlineData("2027-01-01","2026-12-28","2027-01-31")]
    [InlineData("0001-01-01","0001-01-01","0001-02-04")]
    [InlineData("9999-12-01","9999-11-29","9999-12-31")]
    public void RangoSemanasCompleto_Hasta42Dias(string mes,string desde,string hasta)
    {
        var r=ConsultarCalendario.Rango(DateOnly.Parse(mes));Assert.Equal(DateOnly.Parse(desde),r.Desde);Assert.Equal(DateOnly.Parse(hasta),r.Hasta);Assert.InRange(r.Hasta.DayNumber-r.Desde.DayNumber+1,28,42);
    }
    [Fact]
    public async Task TresLecturas_SinPeticionPorCelda_SoloGet()
    {
        using var h=new Handler();using var c=new HttpClient(h){BaseAddress=new("https://api.invalid/")};
        var r=await new ConsultarCalendario().CargarAsync(c,null,null,default);
        Assert.NotNull(r.Vista);Assert.Equal(new DateOnly(2026,11,8),r.Vista.Hoy);Assert.Equal(new DateOnly(2026,11,8),r.Vista.Seleccion);
        Assert.Equal(42,r.Vista.Dias!.Length);Assert.Empty(r.Vista.Habitos!);Assert.Equal(3,h.Paths.Count);
        Assert.Contains("desde=2026-10-26&hasta=2026-12-06",h.Paths[1]);Assert.Contains("fecha=2026-11-08",h.Paths[2]);
    }
    [Theory]
    [InlineData(422,"histórico")] [InlineData(503,"inicializado")] [InlineData(500,"cargar")]
    public async Task RangoFallido_NoOcultaDetalleValido(int status,string texto)
    {
        using var h=new Handler{RangoStatus=status};using var c=new HttpClient(h){BaseAddress=new("https://api.invalid/")};
        var r=await new ConsultarCalendario().CargarAsync(c,null,null,default);
        Assert.NotNull(r.Vista);Assert.Null(r.Vista.Dias);Assert.Contains(texto,r.Vista.ErrorRango);Assert.Empty(r.Vista.Habitos!);Assert.Null(r.Vista.ErrorDetalle);
    }
    [Theory]
    [InlineData(422)] [InlineData(503)]
    public async Task DetalleFallido_NoOcultaConteos(int status)
    {
        using var h=new Handler{DetalleStatus=status};using var c=new HttpClient(h){BaseAddress=new("https://api.invalid/")};
        var r=await new ConsultarCalendario().CargarAsync(c,null,null,default);Assert.NotNull(r.Vista!.Dias);Assert.Null(r.Vista.Habitos);Assert.NotNull(r.Vista.ErrorDetalle);
    }
    [Theory]
    [InlineData("2026-13",null)] [InlineData("no",null)] [InlineData("2026-11","no")] [InlineData("2026-11","2025-01-01")]
    public async Task ParametrosInvalidos_NoConsultanRango(string mes,string? fecha)
    {using var h=new Handler();using var c=new HttpClient(h){BaseAddress=new("https://api.invalid/")};var r=await new ConsultarCalendario().CargarAsync(c,mes,fecha,default);Assert.Null(r.Vista);Assert.NotNull(r.Error);Assert.Single(h.Paths);}
    [Fact]
    public async Task FechaLocalNoDisponible_NoInventaFechaNiConsultaHistorial()
    {using var h=new Handler{LocalStatus=503};using var c=new HttpClient(h){BaseAddress=new("https://api.invalid/")};var r=await new ConsultarCalendario().CargarAsync(c,null,null,default);Assert.Null(r.Vista);Assert.NotNull(r.Error);Assert.Single(h.Paths);}
    [Fact]
    public async Task Desconexion_NoEsVacio()
    {using var h=new Handler{Desconectado=true};using var c=new HttpClient(h){BaseAddress=new("https://api.invalid/")};var r=await new ConsultarCalendario().CargarAsync(c,null,null,default);Assert.Null(r.Vista);Assert.Contains("conectar",r.Error);}
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Unauthorized_RedirigeSinResultadoAcreditado(int posicion)
    {using var h=new Handler{LocalStatus=posicion==0?401:200,RangoStatus=posicion==1?401:200,DetalleStatus=posicion==2?401:200};using var c=new HttpClient(h){BaseAddress=new("https://api.invalid/")};var r=await new ConsultarCalendario().CargarAsync(c,null,null,default);Assert.True(r.LoginRequerido);Assert.Null(r.Vista);}
    private sealed class Handler:HttpMessageHandler
    {
        public int LocalStatus=200,RangoStatus=200,DetalleStatus=200;public bool Desconectado;public List<string> Paths=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get,r.Method);Paths.Add(r.RequestUri!.PathAndQuery);if(Desconectado)throw new HttpRequestException();
            if(r.RequestUri.AbsolutePath.EndsWith("mi-fecha-local"))return Task.FromResult(new HttpResponseMessage((HttpStatusCode)LocalStatus){Content=JsonContent.Create(new{fecha=new DateOnly(2026,11,8)})});
            if(r.RequestUri.AbsolutePath.EndsWith("calendario"))return Task.FromResult(new HttpResponseMessage((HttpStatusCode)RangoStatus){Content=JsonContent.Create(Enumerable.Range(0,42).Select(i=>new DiaResumen(new DateOnly(2026,10,26).AddDays(i),0,0)).ToArray())});
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)DetalleStatus){Content=JsonContent.Create(Array.Empty<HabitoCalendario>())});
        }
    }
}
