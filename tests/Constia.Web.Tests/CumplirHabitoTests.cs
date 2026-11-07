using System.Net;
using System.Net.Http.Json;
using Constia.Web.Habitos;
using Microsoft.Extensions.Caching.Memory;

namespace Constia.Web.Tests;

public sealed class CumplirHabitoTests
{
    private static readonly Guid Habit = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 10, 9);
    private static readonly HabitosHoy Today = new(Date, [new(Habit, "Leer", null, false)]);

    [Fact]
    public async Task DosPestanas_MientrasElPostEstaPendiente_NoGeneranUnaSegundaEscritura()
    {
        using var scenario = new Scenario();
        scenario.Handler.PostRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Handler.Responses.Enqueue(_ => Json(Today));
        scenario.Handler.Responses.Enqueue(_ => Json(new Cumplimiento(Guid.NewGuid(), Date), HttpStatusCode.Created));
        scenario.Handler.Responses.Enqueue(_ => Json(Today));
        scenario.Handler.Responses.Enqueue(_ => Json(Today with { Habitos = [new(Habit, "Leer", null, true)] }));
        var primera = scenario.Service.RegistrarAsync(scenario.Client, scenario.User, Habit, Date, default);
        await scenario.Handler.PostStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var segunda = await scenario.Service.RegistrarAsync(scenario.Client, scenario.User, Habit, Date, default);
        Assert.Contains("Ya hay una solicitud", segunda.Mensaje);
        scenario.Handler.PostRelease.SetResult();
        var result = await primera;
        Assert.Contains("confirmado", result.Mensaje);
        Assert.Single(scenario.Handler.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal("{\"fecha\":\"2026-10-09\"}", scenario.Handler.PostBody);
    }

    [Fact]
    public async Task DuplicadoConfirmado_SePresentaComoPersistido()
    {
        using var scenario = new Scenario();
        scenario.Handler.Responses.Enqueue(_ => Json(Today));
        scenario.Handler.Responses.Enqueue(_ => new(HttpStatusCode.Conflict));
        scenario.Handler.Responses.Enqueue(_ => Json(new[] { new Cumplimiento(Guid.NewGuid(), Date) }));
        scenario.Handler.Responses.Enqueue(_ => Json(Today with { Habitos = [new(Habit, "Leer", null, true)] }));
        var result = await scenario.Service.RegistrarAsync(scenario.Client, scenario.User, Habit, Date, default);
        Assert.Contains("confirmado", result.Mensaje);
        Assert.True(Assert.Single(result.Dia!.Habitos).Cumplido);
    }

    [Fact]
    public async Task ConfirmacionPerdida_SeReconciliaSinReenviar()
    {
        using var scenario = new Scenario();
        scenario.Handler.Responses.Enqueue(_ => Json(Today));
        scenario.Handler.Responses.Enqueue(_ => throw new HttpRequestException("Respuesta perdida"));
        scenario.Handler.Responses.Enqueue(_ => Json(new[] { new Cumplimiento(Guid.NewGuid(), Date) }));
        scenario.Handler.Responses.Enqueue(_ => Json(Today with { Habitos = [new(Habit, "Leer", null, true)] }));
        var result = await scenario.Service.RegistrarAsync(scenario.Client, scenario.User, Habit, Date, default);
        Assert.Contains("confirmado", result.Mensaje);
        Assert.True(Assert.Single(result.Dia!.Habitos).Cumplido);
        Assert.Equal(1, scenario.Handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task LecturaIncierta_PersisteYLaSegundaActivacionSoloComprueba()
    {
        using var scenario = new Scenario();
        scenario.Handler.Responses.Enqueue(_ => Json(Today));
        scenario.Handler.Responses.Enqueue(_ => throw new HttpRequestException());
        scenario.Handler.Responses.Enqueue(_ => new(HttpStatusCode.ServiceUnavailable));
        scenario.Handler.Responses.Enqueue(_ => Json(Today));
        var first = await scenario.Service.RegistrarAsync(scenario.Client, scenario.User, Habit, Date, default);
        Assert.True(scenario.Service.EstaIncierto(scenario.User, Habit, Date));
        Assert.Contains("incierto", first.Mensaje);
        scenario.Handler.Responses.Enqueue(_ => Json(Array.Empty<Cumplimiento>()));
        scenario.Handler.Responses.Enqueue(_ => Json(Today));
        var second = await scenario.Service.RegistrarAsync(scenario.Client, scenario.User, Habit, Date, default);
        Assert.False(scenario.Service.EstaIncierto(scenario.User, Habit, Date));
        Assert.Contains("nueva activación", second.Mensaje);
        Assert.Equal(1, scenario.Handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task Medianoche_NoReinterpretaLaFechaNiEnvia()
    {
        using var scenario = new Scenario();
        scenario.Handler.Responses.Enqueue(_ => Json(Today with { Fecha = Date.AddDays(1) }));
        var result = await scenario.Service.RegistrarAsync(scenario.Client, scenario.User, Habit, Date, default);
        Assert.Contains("fecha", result.Mensaje);
        Assert.DoesNotContain(scenario.Handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task HabitoFueraDeProgramacion_NoEnvia()
    {
        using var scenario = new Scenario();
        scenario.Handler.Responses.Enqueue(_ => Json(Today with { Habitos = [] }));
        var result = await scenario.Service.RegistrarAsync(scenario.Client, scenario.User, Habit, Date, default);
        Assert.Contains("disponible", result.Mensaje);
        Assert.DoesNotContain(scenario.Handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ConflictoSinPersistencia_NoLoPresentaComoRealizado()
    {
        using var scenario = new Scenario();
        scenario.Handler.Responses.Enqueue(_ => Json(Today));
        scenario.Handler.Responses.Enqueue(_ => new(HttpStatusCode.Conflict));
        scenario.Handler.Responses.Enqueue(_ => Json(Array.Empty<Cumplimiento>()));
        scenario.Handler.Responses.Enqueue(_ => Json(new { estado = 1 }));
        scenario.Handler.Responses.Enqueue(_ => Json(Today with { Habitos = [] }));
        var result = await scenario.Service.RegistrarAsync(scenario.Client, scenario.User, Habit, Date, default);
        Assert.DoesNotContain("confirmado", result.Mensaje!);
        Assert.Contains("inactivo", result.Mensaje);
    }

    [Fact]
    public async Task Confirmado_SeConservaAunqueFalleElRefresco()
    {
        using var scenario = new Scenario();
        scenario.Handler.Responses.Enqueue(_ => Json(Today));
        scenario.Handler.Responses.Enqueue(_ => Json(new Cumplimiento(Guid.NewGuid(), Date), HttpStatusCode.Created));
        scenario.Handler.Responses.Enqueue(_ => throw new HttpRequestException());
        var result = await scenario.Service.RegistrarAsync(scenario.Client, scenario.User, Habit, Date, default);
        Assert.Contains("confirmado", result.Mensaje);
        Assert.NotNull(result.Error);
        Assert.Null(result.Dia);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(401)]
    [InlineData(422)]
    [InlineData(503)]
    public async Task RechazoExplicito_NoRegistraNiConsultaHistorial(int status)
    {
        using var scenario = new Scenario();
        scenario.Handler.Responses.Enqueue(_ => Json(Today));
        scenario.Handler.Responses.Enqueue(_ => new((HttpStatusCode)status));
        scenario.Handler.Responses.Enqueue(_ => Json(Today));
        var result = await scenario.Service.RegistrarAsync(scenario.Client, scenario.User, Habit, Date, default);
        Assert.Equal(status == 401, result.LoginRequerido);
        Assert.NotNull(result.Mensaje);
        Assert.DoesNotContain(scenario.Handler.Requests, r => r.Uri.EndsWith("/cumplimientos") && r.Method == HttpMethod.Get);
    }

    internal static HttpResponseMessage Json<T>(T data, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(data) };

    private sealed class Scenario : IDisposable
    {
        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());
        public FakeHandler Handler { get; } = new();
        public HttpClient Client { get; }
        public CumplirHabito Service { get; }
        public Guid User { get; } = Guid.NewGuid();
        public Scenario()
        {
            Client = new(Handler) { BaseAddress = new Uri("https://api.example.invalid/") };
            Service = new(Cache);
        }
        public void Dispose() { Client.Dispose(); Cache.Dispose(); }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public TaskCompletionSource PostStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? PostRelease { get; set; }
        public string? PostBody { get; private set; }
        public Queue<Func<HttpRequestMessage, HttpResponseMessage>> Responses { get; } = new();
        public List<(HttpMethod Method, string Uri)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            var response = Responses.Dequeue();
            if (request.Method == HttpMethod.Post)
            {
                PostBody = await request.Content!.ReadAsStringAsync(cancellation);
                PostStarted.TrySetResult();
                if (PostRelease is not null)
                    await PostRelease.Task.WaitAsync(cancellation);
            }
            return response(request);
        }
    }
}
