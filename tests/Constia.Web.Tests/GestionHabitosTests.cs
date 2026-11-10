using System.Net;
using System.Net.Http.Json;
using Constia.Web.Habitos;
using Microsoft.Extensions.Caching.Memory;

namespace Constia.Web.Tests;

public sealed class GestionHabitosTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly HabitoGestion Activo = new(Id, "Leer", "Un libro", DateTimeOffset.UtcNow, new(2026, 1, 1), 0, [DayOfWeek.Friday], null, null);
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    [Fact]
    public async Task Lista_ConservaOrdenLegadosYDias_SinPedirDetalle()
    {
        using var s = new Scenario();
        s.Handler.Responses.Enqueue(_ => Json(new[] { Activo, Activo with { Id = Guid.NewGuid(), Nombre = "Otro" } }));
        var r = await s.Service.ListarAsync(s.Client, default);
        Assert.Equal(new[] { "Leer", "Otro" }, r.Habitos!.Select(h => h.Nombre));
        Assert.Null(r.Habitos![0].IconoId);
        Assert.Equal([DayOfWeek.Friday], r.Habitos![0].DiasProgramados);
        Assert.Single(s.Handler.Methods);
    }
    [Fact]
    public async Task Vacio_SoloArrayVacioExitoso()
    {
        using var s = new Scenario();
        s.Handler.Responses.Enqueue(_ => Json(Array.Empty<HabitoGestion>()));
        Assert.Empty((await s.Service.ListarAsync(s.Client, default)).Habitos!);
        s.Handler.Responses.Enqueue(_ => Json<HabitoGestion[]?>(null));
        Assert.Null((await s.Service.ListarAsync(s.Client, default)).Habitos);
    }
    [Theory]
    [InlineData(401)] [InlineData(404)] [InlineData(409)] [InlineData(503)] [InlineData(500)]
    public async Task Lista_ErrorNoSeConvierteEnVacio(int status)
    {
        using var s = new Scenario();
        s.Handler.Responses.Enqueue(_ => new((HttpStatusCode)status));
        var r = await s.Service.ListarAsync(s.Client, default);
        Assert.Null(r.Habitos); Assert.NotNull(r.Error); Assert.Equal(status == 401, r.LoginRequerido);
    }
    [Fact]
    public async Task Desactivacion204_ConfirmaAunqueFalleRecarga_YNoRepite()
    {
        using var s = new Scenario(); s.Lista();
        s.Handler.Responses.Enqueue(_ => new(HttpStatusCode.NoContent));
        s.Handler.Responses.Enqueue(_ => new(HttpStatusCode.ServiceUnavailable));
        var r = await s.Service.DesactivarAsync(s.Client, s.User, Id, default);
        Assert.Contains("Desactivación confirmada", r.Mensaje); Assert.Null(r.Habitos);
        s.Lista(); await s.Service.DesactivarAsync(s.Client, s.User, Id, default);
        Assert.Single(s.Handler.Methods, m => m == HttpMethod.Delete);
    }
    [Theory]
    [InlineData(401)] [InlineData(404)] [InlineData(409)] [InlineData(503)]
    public async Task Rechazos_ConservanMensajeDistintoYSinExito(int status)
    {
        using var s = new Scenario(); s.Lista();
        s.Handler.Responses.Enqueue(_ => new((HttpStatusCode)status)); s.Lista();
        var r = await s.Service.DesactivarAsync(s.Client, s.User, Id, default);
        Assert.DoesNotContain("confirmada", r.Mensaje ?? "");
        Assert.Equal(status == 401, r.LoginRequerido);
        Assert.NotNull(r.Error ?? r.Mensaje);
    }
    [Fact]
    public async Task RespuestaPerdida_InactivoAcreditaSoloEstadoFinal()
    {
        using var s = new Scenario(); s.Lista();
        s.Handler.Responses.Enqueue(_ => throw new HttpRequestException());
        s.Handler.Responses.Enqueue(_ => Json(Activo with { Estado = 1 })); s.Lista();
        var r = await s.Service.DesactivarAsync(s.Client, s.User, Id, default);
        Assert.Contains("está inactivo", r.Mensaje); Assert.DoesNotContain("confirmada", r.Mensaje!);
        Assert.Empty(s.Service.Inciertos(s.User));
    }
    [Fact]
    public async Task IncertidumbrePersiste_NoReenviaYAislaUsuarios()
    {
        using var s = new Scenario(); s.Lista();
        s.Handler.Responses.Enqueue(_ => throw new HttpRequestException());
        s.Handler.Responses.Enqueue(_ => new(HttpStatusCode.ServiceUnavailable)); s.Lista();
        await s.Service.DesactivarAsync(s.Client, s.User, Id, default);
        Assert.Single(s.Service.Inciertos(s.User)); Assert.Empty(s.Service.Inciertos(Guid.NewGuid()));
        s.Handler.Responses.Enqueue(_ => Json(Activo)); s.Lista();
        var r = await s.Service.DesactivarAsync(s.Client, s.User, Id, default);
        Assert.Single(s.Service.Inciertos(s.User)); Assert.Contains("incierto", r.Mensaje);
        Assert.Single(s.Handler.Methods, m => m == HttpMethod.Delete);
    }
    [Fact]
    public async Task NoCandidato_NoCreaEstadoNiEnviaDelete()
    {
        using var s = new Scenario(); s.Lista();
        var r = await s.Service.DesactivarAsync(s.Client, s.User, Guid.NewGuid(), default);
        Assert.DoesNotContain(HttpMethod.Delete, s.Handler.Methods); Assert.Equal(0, s.Cache.Count);
        Assert.NotNull(r.Mensaje);
    }
    [Fact]
    public async Task DosPestanas_CompartenCoordinadorAtomico()
    {
        using var s = new Scenario(); s.Lista();
        s.Handler.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Handler.Responses.Enqueue(_ => new(HttpStatusCode.NoContent));
        var primera = s.Service.DesactivarAsync(s.Client, s.User, Id, default);
        await s.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        s.Lista(); await s.Service.DesactivarAsync(s.Client, s.User, Id, default);
        s.Lista(); s.Handler.Release.SetResult(); await primera;
        Assert.Single(s.Handler.Methods, m => m == HttpMethod.Delete);
    }
    [Fact]
    public async Task ComprobarSinEstado_NoCreaCoordinadorNiDesactiva()
    {
        using var s = new Scenario(); s.Lista();
        await s.Service.DesactivarAsync(s.Client,s.User,Id,default,soloComprobar:true);
        Assert.Equal(0,s.Cache.Count); Assert.DoesNotContain(HttpMethod.Delete,s.Handler.Methods);
    }
    [Fact]
    public async Task RechazoSeguidoDe401_RedirigeAlLogin()
    {
        using var s = new Scenario();s.Lista();
        s.Handler.Responses.Enqueue(_=>new(HttpStatusCode.Conflict));
        s.Handler.Responses.Enqueue(_=>new(HttpStatusCode.Unauthorized));
        Assert.True((await s.Service.DesactivarAsync(s.Client,s.User,Id,default)).LoginRequerido);
    }
    [Theory]
    [InlineData("{")] [InlineData("null")] [InlineData("{}")]
    public async Task JsonInvalido_NoAcreditaListaVacia(string body)
    {
        using var s=new Scenario();
        s.Handler.Responses.Enqueue(_=>new(HttpStatusCode.OK){Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")});
        var r=await s.Service.ListarAsync(s.Client,default);Assert.Null(r.Habitos);Assert.NotNull(r.Error);
    }
    [Fact]
    public async Task UsuariosIndependientes_NoCompartenResultadoIncierto()
    {
        using var s=new Scenario();s.Lista();
        s.Handler.Responses.Enqueue(_=>throw new HttpRequestException());
        s.Handler.Responses.Enqueue(_=>new(HttpStatusCode.ServiceUnavailable));s.Lista();
        await s.Service.DesactivarAsync(s.Client,s.User,Id,default);
        var otro=Guid.NewGuid();s.Lista();s.Handler.Responses.Enqueue(_=>new(HttpStatusCode.NoContent));s.Lista();
        var r=await s.Service.DesactivarAsync(s.Client,otro,Id,default);
        Assert.Contains("confirmada",r.Mensaje);Assert.Single(s.Service.Inciertos(s.User));Assert.Empty(s.Service.Inciertos(otro));
        Assert.Equal(2,s.Handler.Methods.Count(m=>m==HttpMethod.Delete));
    }
    [Fact]
    public async Task Inactivos_ReintentoSoloLeeYConservaLegado()
    {
        using var s = new Scenario();
        s.Handler.Responses.Enqueue(_ => new(HttpStatusCode.ServiceUnavailable));
        s.Handler.Responses.Enqueue(_ => Json(new[] { Activo with { Estado = 1 } }));
        var error = await s.Service.ListarInactivosAsync(s.Client, default);
        Assert.Null(error.Habitos);
        Assert.NotNull(error.Error);
        var habito = Assert.Single((await s.Service.ListarInactivosAsync(s.Client, default)).Habitos!);
        Assert.Equal(Activo.Descripcion, habito.Descripcion);
        Assert.Equal(Activo.DiasProgramados, habito.DiasProgramados);
        Assert.Null(habito.IconoId);
        Assert.Null(habito.CategoriaId);
        Assert.Equal(2, s.Handler.Methods.Count);
        Assert.All(s.Handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
        Assert.All(s.Handler.Paths, path => Assert.Equal("/api/habitos/inactivos", path));
    }

    [Fact]
    public async Task Inactivos_DistingueVacioActivoInesperadoYSesionVencida()
    {
        using var s = new Scenario();
        s.Handler.Responses.Enqueue(_ => Json(Array.Empty<HabitoGestion>()));
        s.Handler.Responses.Enqueue(_ => Json(new[] { Activo }));
        s.Handler.Responses.Enqueue(_ => new(HttpStatusCode.Unauthorized));
        Assert.Empty((await s.Service.ListarInactivosAsync(s.Client, default)).Habitos!);
        Assert.Null((await s.Service.ListarInactivosAsync(s.Client, default)).Habitos);
        Assert.True((await s.Service.ListarInactivosAsync(s.Client, default)).LoginRequerido);
    }

    private sealed class Scenario : IDisposable
    {
        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());
        public Handler Handler { get; } = new();
        public HttpClient Client { get; }
        public GestionHabitos Service { get; }
        public Guid User { get; } = Guid.NewGuid();
        public Scenario() { Client = new(Handler) { BaseAddress = new("https://api.example.invalid") }; Service = new(Cache); }
        public void Lista() => Handler.Responses.Enqueue(_ => Json(new[] { Activo }));
        public void Dispose() { Client.Dispose(); Cache.Dispose(); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public Queue<Func<HttpRequestMessage,HttpResponseMessage>> Responses { get; } = new();
        public List<HttpMethod> Methods { get; } = [];
        public List<string> Paths { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Release;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            Methods.Add(req.Method); Paths.Add(req.RequestUri!.AbsolutePath); var result = Responses.Dequeue();
            if(req.Method == HttpMethod.Delete) { Started.TrySetResult(); if(Release is not null) await Release.Task.WaitAsync(ct); }
            return result(req);
        }
    }
}
