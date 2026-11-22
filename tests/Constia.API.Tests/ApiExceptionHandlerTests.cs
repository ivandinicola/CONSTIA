using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.IO.Pipelines;
using System.Text.Json;
using Constia.Application.Habitos;
using Constia.Application.Usuarios;
using Constia.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Constia.API.Tests;

public sealed class ApiExceptionHandlerTests(ITestOutputHelper output)
{
    internal const string Marcador = "API003-SENSITIVE-TEST-ONLY";

    [Theory]
    [InlineData("Development", "application/json")]
    [InlineData("Development", "text/html")]
    [InlineData("Development", "application/xml")]
    [InlineData("Production", "application/json")]
    [InlineData("Production", "text/html")]
    [InlineData("Production", "application/xml")]
    public async Task ExcepcionInesperada_Devuelve500SeguroSinDependerDeAccept(string ambiente, string accept)
    {
        using var baseHost = new JwtApiFactory();
        using var host = CrearHost(baseHost, ambiente);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        using var response = await client.GetAsync("/__tests/api003/fallo?dato=" + Marcador);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("No pudimos completar la operación.", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("internal_error", json.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        Assert.Equal(new[] { "code", "status", "title", "traceId" },
            json.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Marcador, body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task RegistroPropio_SoloMetadatosSeguros_UnaVezYSinDiagnosticoDuplicado()
    {
        using var baseHost = new JwtApiFactory();
        var logs = new Registros();
        using var host = CrearHost(baseHost, "Development", logs);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Synthetic " + Marcador);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "session=" + Marcador);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Request-ID", Marcador);
        using var response = await client.GetAsync("/__tests/api003/fallo?dato=" + Marcador);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var entry = Assert.Single(logs.Propios);
        Assert.Equal(3003, entry.Id.Id);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(Marcador, entry.Message);
        Assert.Equal("__tests/api003/fallo", entry.Fields["Route"]);
        Assert.Equal("GET", entry.Fields["Method"]);
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.Fields["ExceptionType"]);
        Assert.Equal(500, entry.Fields["StatusCode"]);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(body.RootElement.GetProperty("traceId").GetString(), entry.Fields["TraceId"]);
        Assert.DoesNotContain(Marcador, body.RootElement.GetProperty("traceId").GetString()!);
        Assert.DoesNotContain(logs.Entries, e => e.Exception is not null);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(413)]
    [InlineData(415)]
    public async Task ErrorDeProtocolo_ConservaEstadoSinExponerMensaje(int status)
    {
        using var baseHost = new JwtApiFactory();
        using var host = CrearHost(baseHost, "Production");
        using var client = host.CreateClient();
        using var response = await client.GetAsync($"/__tests/api003/protocolo/{status}");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(status, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("La solicitud no es válida.", body.RootElement.GetProperty("title").GetString());
        Assert.False(body.RootElement.TryGetProperty("code", out _));
        Assert.DoesNotContain(Marcador, body.RootElement.GetRawText());
    }

    [Theory]
    [InlineData("argumento")]
    [InlineData("sql")]
    [InlineData("cancelacion-interna")]
    public async Task ExcepcionNoClasificada_NoSeConvierteEnErrorDeNegocio(string tipo)
    {
        using var baseHost = new JwtApiFactory();
        using var host = CrearHost(baseHost, "Production");
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/__tests/api003/no-clasificada/" + tipo);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("internal_error", body);
        Assert.DoesNotContain(Marcador, body);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestAbortedCancelado_NoProduce500NiLogPropio(bool io)
    {
        using var baseHost = new JwtApiFactory();
        var logs = new Registros();
        using var host = CrearHost(baseHost, "Production", logs);
        using var client = host.CreateClient();
        using var response = await client.GetAsync($"/__tests/api003/cancelada/{io}");
        Assert.Equal(499, (int)response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.Empty(logs.Propios);
    }

    [Fact]
    public async Task FalloTrasGuardarEnRepositorio_NoReejecutaLaAccionNiLaEscritura()
    {
        using var baseHost = new JwtApiFactory();
        var escrituras = 0;
        using var host = baseHost.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var original = services.Last(s => s.ServiceType == typeof(IHabitoRepository));
            services.Remove(original);
            services.AddScoped<IHabitoRepository>(sp => new FalloTrasGuardar(
                (IHabitoRepository)original.ImplementationFactory!(sp), () => Interlocked.Increment(ref escrituras)));
        }));
        using var client = host.CreateClient();
        var tokens = host.Services.GetRequiredService<IAccessTokenService>();
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            tokens.Emitir(baseHost.Usuario.Id, baseHost.Usuario.Nombre, baseHost.Usuario.Email).Value);
        using var response = await client.PostAsJsonAsync("/api/habitos", new
        {
            nombre = "Leer", categoriaId = CatalogoCategoriasHabito.Todas[0].Id,
            iconoId = "libro", diasProgramados = new[] { 1, 3, 5 }
        });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, escrituras);
        Assert.Equal("Leer", Assert.Single(baseHost.HabitosPersistidos).Nombre);
        Assert.DoesNotContain(Marcador, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Kestrel_RespuestaIniciada_ConservaHeadersYPrefijoYAbortaSinOtroCuerpo(string ambiente)
    {
        using var baseHost = new JwtApiFactory();
        var logs = new Registros();
        Uri address;
        await using (var host = CrearHost(baseHost, ambiente, logs))
        {
            host.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            using var client = host.CreateClient();
            address = client.BaseAddress!;
            using var response = await client.GetAsync("/__tests/api003/iniciada", HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("iniciada", Assert.Single(response.Headers.GetValues("X-Test-Phase")));
            Assert.Null(response.Headers.CacheControl);
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var received = new MemoryStream();
            await Assert.ThrowsAnyAsync<IOException>(() => stream.CopyToAsync(received));
            Assert.Equal("prefijo", System.Text.Encoding.UTF8.GetString(received.ToArray()));
            Assert.Empty(logs.Propios);
            Assert.Contains(logs.Entries, e => e.Exception is not null);
        }
        await VerificarPuertoCerrado(address);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Kestrel_FalloAlEscribirError_AbortaSinFiltrarNiReintentar(string ambiente)
    {
        using var baseHost = new JwtApiFactory();
        var logs = new Registros();
        var estado = new EstadoFallo();
        Uri address;
        await using (var host = CrearHost(baseHost, ambiente, logs, estado))
        {
            host.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            using var client = host.CreateClient();
            address = client.BaseAddress!;
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                using var response = await client.GetAsync("/__tests/api003/escritura-error");
                var body = await response.Content.ReadAsStringAsync();
                output.WriteLine("Respuesta inesperada: status={0}; contiene marcador sintético={1}; intentos={2}",
                    (int)response.StatusCode, body.Contains(Marcador), estado.IntentosDeEscritura);
            });
            Assert.Equal(1, estado.IntentosDeEscritura);
            Assert.Equal(1, estado.Ejecuciones);
            Assert.Single(logs.Propios);
        }
        await VerificarPuertoCerrado(address);
    }

    [Fact]
    public async Task Kestrel_CancelacionRealDelCliente_SeObservaSinLogDeErrorPropio()
    {
        using var baseHost = new JwtApiFactory();
        var logs = new Registros();
        var estado = new EstadoFallo();
        Uri address;
        await using (var host = CrearHost(baseHost, "Production", logs, estado))
        {
            host.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            using var client = host.CreateClient();
            address = client.BaseAddress!;
            using var cancel = new CancellationTokenSource();
            var solicitud = client.GetAsync("/__tests/api003/esperar", cancel.Token);
            await estado.Entrada.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => solicitud);
            await estado.Cancelada.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Empty(logs.Propios);
        await VerificarPuertoCerrado(address);
    }

    private async Task VerificarPuertoCerrado(Uri address)
    {
        using var socket = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => socket.ConnectAsync(IPAddress.Loopback, address.Port));
        output.WriteLine("Kestrel loopback:{0}: cerrado tras DisposeAsync; sin proceso externo.", address.Port);
    }

    private static WebApplicationFactory<Program> CrearHost(JwtApiFactory baseHost, string ambiente,
        Registros? logs = null, EstadoFallo? estado = null) =>
        baseHost.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(ambiente);
            if (logs is not null) builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(estado ?? new EstadoFallo());
                services.AddControllers().AddApplicationPart(typeof(Api003FallosController).Assembly);
            });
        });

    private sealed class FalloTrasGuardar(IHabitoRepository inner, Action guardado) : IHabitoRepository
    {
        public async Task AgregarAsync(Habito habito, CancellationToken ct)
        {
            await inner.AgregarAsync(habito, ct);
            guardado();
            throw new InvalidOperationException(Marcador);
        }
        public Task<IReadOnlyList<Habito>> ListarActivosPorUsuarioAsync(Guid usuario, CancellationToken ct) => inner.ListarActivosPorUsuarioAsync(usuario, ct);
        public Task<IReadOnlyList<Habito>> ListarInactivosPorUsuarioAsync(Guid usuario, CancellationToken ct) => inner.ListarInactivosPorUsuarioAsync(usuario, ct);
        public Task<Habito?> BuscarPorIdYUsuarioAsync(Guid id, Guid usuario, CancellationToken ct) => inner.BuscarPorIdYUsuarioAsync(id, usuario, ct);
        public Task<Habito?> BuscarParaModificarPorIdYUsuarioAsync(Guid id, Guid usuario, CancellationToken ct) => inner.BuscarParaModificarPorIdYUsuarioAsync(id, usuario, ct);
        public Task GuardarCambiosAsync(Habito habito, CancellationToken ct) => inner.GuardarCambiosAsync(habito, ct);
    }

    private sealed record Registro(string Category, LogLevel Level, EventId Id, string Message,
        Exception? Exception, Dictionary<string, object?> Fields);

    private sealed class Registros : ILoggerProvider
    {
        public ConcurrentQueue<Registro> Entries { get; } = new();
        public IEnumerable<Registro> Propios => Entries.Where(e => e.Category == "Constia.API.Errors.ApiExceptionHandler");
        public ILogger CreateLogger(string categoryName) => new Captura(categoryName, Entries);
        public void Dispose() { }
        private sealed class Captura(string category, ConcurrentQueue<Registro> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => entries.Enqueue(new(category, level, id,
                    formatter(state, exception), exception,
                    (state as IEnumerable<KeyValuePair<string, object?>>)?.ToDictionary(x => x.Key, x => x.Value) ?? new()));
        }
    }
}

// Solo se descubre en el host de estas pruebas mediante ApplicationPart.
[ApiController]
[AllowAnonymous]
[Route("__tests/api003")]
public sealed class Api003FallosController(EstadoFallo estado) : ControllerBase
{
    [HttpGet("fallo")]
    public IActionResult Fallar() => throw new InvalidOperationException(
        ApiExceptionHandlerTests.Marcador, new Exception(ApiExceptionHandlerTests.Marcador));

    [HttpGet("protocolo/{status:int}")]
    public IActionResult Protocolo(int status) => throw new BadHttpRequestException(ApiExceptionHandlerTests.Marcador, status);

    [HttpGet("no-clasificada/{tipo}")]
    public IActionResult NoClasificada(string tipo) => throw tipo switch
    {
        "argumento" => new ArgumentException(ApiExceptionHandlerTests.Marcador),
        "sql" => new DbUpdateException(ApiExceptionHandlerTests.Marcador),
        _ => new OperationCanceledException(ApiExceptionHandlerTests.Marcador)
    };

    [HttpGet("cancelada/{io:bool}")]
    public IActionResult Cancelada(bool io)
    {
        HttpContext.RequestAborted = new CancellationToken(true);
        if (io) throw new IOException(ApiExceptionHandlerTests.Marcador);
        throw new OperationCanceledException(HttpContext.RequestAborted);
    }

    [HttpGet("iniciada")]
    public async Task Iniciada()
    {
        Response.StatusCode = 202;
        Response.ContentType = "text/plain";
        Response.Headers["X-Test-Phase"] = "iniciada";
        await Response.WriteAsync("prefijo");
        await Response.Body.FlushAsync();
        throw new InvalidOperationException(ApiExceptionHandlerTests.Marcador);
    }

    [HttpGet("escritura-error")]
    public IActionResult ErrorDeEscritura()
    {
        Interlocked.Increment(ref estado.Ejecuciones);
        var original = HttpContext.Features.Get<IHttpResponseBodyFeature>()!;
        HttpContext.Features.Set<IHttpResponseBodyFeature>(new FallaPrimeraEscritura(original, estado));
        throw new InvalidOperationException(ApiExceptionHandlerTests.Marcador);
    }

    [HttpGet("esperar")]
    public async Task Esperar()
    {
        estado.Entrada.TrySetResult();
        try { await Task.Delay(Timeout.Infinite, HttpContext.RequestAborted); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            estado.Cancelada.TrySetResult();
            throw;
        }
    }

    private sealed class FallaPrimeraEscritura(IHttpResponseBodyFeature inner, EstadoFallo estado) : IHttpResponseBodyFeature
    {
        public Stream Stream => inner.Stream;
        public PipeWriter Writer => Interlocked.Increment(ref estado.IntentosDeEscritura) == 1
            ? throw new IOException(ApiExceptionHandlerTests.Marcador)
            : inner.Writer;
        public void DisableBuffering() => inner.DisableBuffering();
        public Task StartAsync(CancellationToken ct = default) => inner.StartAsync(ct);
        public Task CompleteAsync() => inner.CompleteAsync();
        public Task SendFileAsync(string path, long offset, long? count, CancellationToken ct = default) => inner.SendFileAsync(path, offset, count, ct);
    }
}

public sealed class EstadoFallo
{
    public int IntentosDeEscritura;
    public int Ejecuciones;
    public TaskCompletionSource Entrada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Cancelada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
