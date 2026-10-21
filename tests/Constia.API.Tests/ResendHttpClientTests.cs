using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Constia.Application.Correo;
using Constia.Infrastructure.Correo;
using Constia.API.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Constia.API.Tests;

public sealed class ResendHttpClientTests
{
    internal static MensajeVerificacionCorreo Mensaje() => new PreparadorMensajeVerificacion(
        ResendConfiguracionTests.Validas()).Preparar(Guid.Parse("11111111-2222-3333-4444-555555555555"),
        "destino@example.invalid", "NOMBRE-PRIVADO", new string('A', 43),
        DateTimeOffset.Parse("2026-10-21T00:00:00Z"), DateTimeOffset.Parse("2026-10-22T00:00:00Z"));

    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> run) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Interlocked.Increment(ref Calls); return run(request, ct); }
    }

    private static HttpResponseMessage Respuesta(int status, string text) =>
        new((HttpStatusCode)status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    private static ResendEmailClient Cliente(Handler h, TimeProvider? clock = null, OpcionesResend? opciones = null) =>
        new(new HttpClient(h) { Timeout = Timeout.InfiniteTimeSpan }, opciones ?? ResendConfiguracionTests.Validas(), clock ?? TimeProvider.System);

    [Fact]
    public async Task DosInvocaciones_SolicitudesNuevasMismosBytesYCabeceras()
    {
        var bodies = new List<byte[]>(); var requests = new List<HttpRequestMessage>();
        var m = Mensaje();
        var h = new Handler(async (r, ct) =>
        {
            requests.Add(r);
            Assert.Equal(HttpMethod.Post, r.Method);
            Assert.Equal("https://api.resend.com/emails", r.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", r.Headers.Authorization!.Scheme);
            Assert.Equal("synthetic-key-not-a-secret", r.Headers.Authorization.Parameter);
            Assert.Equal(m.ClaveIdempotencia, Assert.Single(r.Headers.GetValues("Idempotency-Key")));
            Assert.Equal("application/json", r.Content!.Headers.ContentType!.MediaType);
            bodies.Add(await r.Content.ReadAsByteArrayAsync(ct));
            return Respuesta(200, "{\"id\":\"opaque-not-uuid\"}");
        });
        var client = Cliente(h);
        foreach(var _ in Enumerable.Range(0,2))
        {
            var result = await client.EnviarAsync(m, default);
            Assert.Equal(EstadoEntregaCorreo.AceptadaPorProveedor, result.Estado);
            Assert.Equal("opaque-not-uuid", result.IdProveedor);
        }
        Assert.Equal(2, h.Calls); Assert.NotSame(requests[0], requests[1]); Assert.Equal(bodies[0], bodies[1]);
        using var doc = JsonDocument.Parse(bodies[0]); var json = doc.RootElement;
        Assert.Equal(new[] {"from","html","subject","text","to"}, json.EnumerateObject().Select(p=>p.Name).Order().ToArray());
        Assert.Equal("CONSTIA <correo@example.invalid>", json.GetProperty("from").GetString());
        Assert.Equal(m.Destinatario, Assert.Single(json.GetProperty("to").EnumerateArray()).GetString());
        Assert.Equal(m.Html, json.GetProperty("html").GetString());
        Assert.Equal(m.TextoPlano, json.GetProperty("text").GetString());
        Assert.Equal(m.Asunto, json.GetProperty("subject").GetString());
    }

    [Theory]
    [InlineData(400, "validation_error", EstadoEntregaCorreo.RechazadaDefinitivamente)]
    [InlineData(400, "invalid_idempotency_key", EstadoEntregaCorreo.RechazadaDefinitivamente)]
    [InlineData(401, "missing_api_key", EstadoEntregaCorreo.ProveedorNoDisponible)]
    [InlineData(403, "validation_error", EstadoEntregaCorreo.ProveedorNoDisponible)]
    [InlineData(409, "invalid_idempotent_request", EstadoEntregaCorreo.RechazadaDefinitivamente)]
    [InlineData(409, "concurrent_idempotent_requests", EstadoEntregaCorreo.FalloTemporal)]
    [InlineData(422, "invalid_parameter", EstadoEntregaCorreo.RechazadaDefinitivamente)]
    [InlineData(429, "rate_limit_exceeded", EstadoEntregaCorreo.FalloTemporal)]
    [InlineData(429, "daily_quota_exceeded", EstadoEntregaCorreo.ProveedorNoDisponible)]
    [InlineData(429, "monthly_quota_exceeded", EstadoEntregaCorreo.ProveedorNoDisponible)]
    [InlineData(500, "application_error", EstadoEntregaCorreo.ResultadoIncierto)]
    [InlineData(503, "service_unavailable", EstadoEntregaCorreo.ResultadoIncierto)]
    [InlineData(409, "unknown", EstadoEntregaCorreo.ResultadoIncierto)]
    [InlineData(429, "unknown", EstadoEntregaCorreo.ResultadoIncierto)]
    [InlineData(418, "unknown", EstadoEntregaCorreo.ResultadoIncierto)]
    public async Task Matriz_UnIntentoSinFiltrarMessage(int status, string code, EstadoEntregaCorreo expected)
    {
        var h = new Handler((_,_) => Task.FromResult(Respuesta(status,
            JsonSerializer.Serialize(new {name=code,message="TOKEN-PRIVADO destino@example.invalid synthetic-key-not-a-secret"}))));
        var r = await Cliente(h).EnviarAsync(Mensaje(),default);
        Assert.Equal(expected,r.Estado); Assert.Equal(status,r.HttpStatus); Assert.Equal(1,h.Calls);
        Assert.DoesNotContain("PRIVADO",r.ToString());
        Assert.DoesNotContain("destino@",r.ToString());
        if(code=="unknown") Assert.False(PoliticaReintentoCorreo.EsCandidatoMismaSolicitud(r));
    }

    [Theory]
    [InlineData(200, "")]
    [InlineData(200, "not-json")]
    [InlineData(200, "{}")]
    [InlineData(200, "{\"id\":12}")]
    [InlineData(200, "{\"id\":\"\"}")]
    [InlineData(200, "{\"id\":\"x\\r\\nsecret\"}")]
    [InlineData(201, "{\"id\":\"opaque\"}")]
    [InlineData(204, "")]
    [InlineData(302, "")]
    public async Task RespuestaInesperada_NoAfirmaAceptacion(int status,string body)
    {
        var h=new Handler((_,_)=>Task.FromResult(Respuesta(status,body)));
        var r=await Cliente(h).EnviarAsync(Mensaje(),default);
        Assert.Equal(EstadoEntregaCorreo.ResultadoIncierto,r.Estado);
        Assert.Equal(1,h.Calls);
    }

    [Fact]
    public async Task RetryAfter_NoSeAcortaYFechaHttpSeInterpreta()
    {
        var clock=new ManualClock();
        foreach(var header in new[]{"7200",clock.GetUtcNow().AddHours(2).ToString("R")})
        {
            var h=new Handler((_,_)=>{
                var response=Respuesta(429,"{\"name\":\"rate_limit_exceeded\"}");
                response.Headers.TryAddWithoutValidation("Retry-After",header); return Task.FromResult(response);
            });
            var r=await Cliente(h,clock).EnviarAsync(Mensaje(),default);
            Assert.Equal(TimeSpan.FromHours(2),r.EsperaMinima); Assert.Equal(1,h.Calls);
        }
        foreach(var header in new[]{"invalid","99999999999999999999999999"})
        {
            var h=new Handler((_,_)=>{var r=Respuesta(429,"{\"name\":\"rate_limit_exceeded\"}");
                r.Headers.TryAddWithoutValidation("Retry-After",header);return Task.FromResult(r);});
            Assert.Null((await Cliente(h,clock).EnviarAsync(Mensaje(),default)).EsperaMinima);
        }
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError,EstadoEntregaCorreo.FalloTemporal)]
    [InlineData(HttpRequestError.SecureConnectionError,EstadoEntregaCorreo.ProveedorNoDisponible)]
    [InlineData(HttpRequestError.ConnectionError,EstadoEntregaCorreo.ResultadoIncierto)]
    public async Task RedDnsTls_SinExcepcionSensible(HttpRequestError error,EstadoEntregaCorreo estado)
    {
        var h=new Handler((_,_)=>throw new HttpRequestException(error,"TOKEN-PRIVADO",null));
        var r=await Cliente(h).EnviarAsync(Mensaje(),default);
        Assert.Equal(estado,r.Estado);Assert.Equal(1,h.Calls);Assert.DoesNotContain("PRIVADO",r.ToString());
    }

    [Fact]
    public async Task CancelacionPrevia_CeroIntentos_YDuranteEnvioIncierta()
    {
        using var ct=new CancellationTokenSource();ct.Cancel();
        var h=new Handler((_,_)=>throw new InvalidOperationException("No debe ejecutarse"));
        Assert.Equal(EstadoEntregaCorreo.CanceladaAntesDelEnvio,(await Cliente(h).EnviarAsync(Mensaje(),ct.Token)).Estado);
        Assert.Equal(0,h.Calls);
        using var during=new CancellationTokenSource();
        var started=new Handler((_,token)=>{during.Cancel();return Task.FromCanceled<HttpResponseMessage>(token);});
        var r=await Cliente(started).EnviarAsync(Mensaje(),during.Token);
        Assert.Equal(EstadoEntregaCorreo.ResultadoIncierto,r.Estado);
        Assert.Equal(MotivoEntregaCorreo.Cancelacion,r.Motivo);
    }

    [Fact]
    public async Task TimeoutCubreEnvioYLecturaSinEsperasReales()
    {
        foreach(var body in new[]{false,true})
        {
            var clock=new ManualClock();
            var h=new Handler(async (_,ct)=>{
                if(body) return new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new BlockingStream(clock))};
                clock.Fire(); await Task.Delay(Timeout.Infinite,ct);
                throw new InvalidOperationException();
            });
            var r=await Cliente(h,clock).EnviarAsync(Mensaje(),default);
            Assert.Equal(EstadoEntregaCorreo.ResultadoIncierto,r.Estado);Assert.Equal(MotivoEntregaCorreo.Timeout,r.Motivo);
            Assert.Equal(1,h.Calls);
        }
    }

    [Fact]
    public async Task ConfiguracionInvalidaODeshabilitada_NoLlamaHandler()
    {
        foreach(var opts in new[]{new OpcionesResend(),new OpcionesResend{Enabled=true}})
        {
            var h=new Handler((_,_)=>throw new InvalidOperationException());
            Assert.Equal(EstadoEntregaCorreo.ProveedorNoDisponible,(await Cliente(h,opciones:opts).EnviarAsync(Mensaje(),default)).Estado);
            Assert.Equal(0,h.Calls);
        }
    }

    [Fact]
    public async Task LecturaAcotada_ConYSinContentLength()
    {
        foreach(var declared in new[]{false,true})
        {
            var stream=new CountingStream(20000);
            var h=new Handler((_,_)=>{
                var c=new StreamContent(stream);
                if(declared)c.Headers.ContentLength=20000;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=c});
            });
            var r=await Cliente(h).EnviarAsync(Mensaje(),default);
            Assert.Equal(MotivoEntregaCorreo.RespuestaDemasiadoGrande,r.Motivo);
            Assert.Equal(EstadoEntregaCorreo.ResultadoIncierto,r.Estado);
            Assert.InRange(stream.BytesRead,0,16385);
            if(declared)Assert.Equal(0,stream.BytesRead);else Assert.Equal(16385,stream.BytesRead);
        }
    }

    [Fact]
    public async Task Composicion_SinRedirectCookiesCertificadosEspecialesNiLogsSensibles()
    {
        var logs=new CaptureLogs();var services=new ServiceCollection();
        services.AddLogging(b=>b.AddProvider(logs));services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddResendC1(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["Resend:Enabled"]="true",["Resend:ApiKey"]="synthetic-key-not-a-secret",
            ["Resend:FromAddress"]="correo@example.invalid",["VerificacionCorreo:PublicUrl"]="https://constia.example"
        }).Build());
        HttpMessageHandler? original=null;
        var fake=new Handler((_,_)=>Task.FromResult(Respuesta(302,"TOKEN-PRIVADO")));
        services.Configure<HttpClientFactoryOptions>(ResendC1ServiceCollectionExtensions.NombreCliente,o=>
            o.HttpMessageHandlerBuilderActions.Add(b=>{original=b.PrimaryHandler;b.PrimaryHandler=fake;}));
        using var provider=services.BuildServiceProvider();
        var result=await provider.GetRequiredService<ITransporteCorreoVerificacion>().EnviarAsync(Mensaje(),default);
        var handler=Assert.IsType<SocketsHttpHandler>(original);
        Assert.False(handler.AllowAutoRedirect);Assert.False(handler.UseCookies);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.Equal(EstadoEntregaCorreo.ResultadoIncierto,result.Estado);Assert.Equal(1,fake.Calls);
        Assert.DoesNotContain(logs.Text,s=>new[]{"TOKEN-PRIVADO","synthetic-key-not-a-secret","destino@example.invalid","NOMBRE-PRIVADO",new string('A',43),"Confirmá tu correo"}.Any(s.Contains));
        original!.Dispose();
    }


    [Fact]
    public async Task LimiteExacto_AceptaHasta16KiBYRechazaUnByteMas()
    {
        foreach(var size in new[]{16383,16384,16385})
        {
            var prefix="{\"id\":\"opaque\"}";
            var body=prefix+new string(' ',size-Encoding.UTF8.GetByteCount(prefix));
            var h=new Handler((_,_)=>Task.FromResult(Respuesta(200,body)));
            var r=await Cliente(h).EnviarAsync(Mensaje(),default);
            Assert.Equal(size<=16384?EstadoEntregaCorreo.AceptadaPorProveedor:EstadoEntregaCorreo.ResultadoIncierto,r.Estado);
            Assert.Equal(1,h.Calls);
        }
    }

    [Fact]
    public async Task InvocacionesConcurrentes_ConservanClaveYBytesSinRetryInterno()
    {
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var items=new System.Collections.Concurrent.ConcurrentBag<(string Key,byte[] Body)>();
        var h=new Handler(async(r,ct)=>{
            items.Add((Assert.Single(r.Headers.GetValues("Idempotency-Key")),await r.Content!.ReadAsByteArrayAsync(ct)));
            if(items.Count==2)ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5),ct);
            return Respuesta(409,"{\"name\":\"concurrent_idempotent_requests\"}");
        });
        var c=Cliente(h);var m=Mensaje();
        var results=await Task.WhenAll(c.EnviarAsync(m,default),c.EnviarAsync(m,default));
        Assert.All(results,r=>Assert.Equal(EstadoEntregaCorreo.FalloTemporal,r.Estado));
        Assert.Equal(2,h.Calls);var snapshots=items.ToArray();
        Assert.Equal(snapshots[0].Key,snapshots[1].Key);Assert.Equal(snapshots[0].Body,snapshots[1].Body);
    }

    [Fact]
    public async Task ErroresYExcepciones_NoAparecenEnLogsAutomaticos()
    {
        var logs=new CaptureLogs();var services=new ServiceCollection();
        services.AddLogging(b=>b.AddProvider(logs));services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddResendC1(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["Resend:Enabled"]="true",["Resend:ApiKey"]="synthetic-key-not-a-secret",
            ["Resend:FromAddress"]="correo@example.invalid",["VerificacionCorreo:PublicUrl"]="https://constia.example"
        }).Build());
        var mode=0;
        var fake=new Handler((_,_)=>mode==0
            ?Task.FromResult(Respuesta(400,"{\"name\":\"validation_error\",\"message\":\"TOKEN-PRIVADO destino@example.invalid\"}"))
            :throw new HttpRequestException(HttpRequestError.ConnectionError,
                "synthetic-key-not-a-secret TOKEN-PRIVADO destino@example.invalid",null));
        services.Configure<HttpClientFactoryOptions>(ResendC1ServiceCollectionExtensions.NombreCliente,o=>
            o.HttpMessageHandlerBuilderActions.Add(b=>b.PrimaryHandler=fake));
        using var provider=services.BuildServiceProvider();var c=provider.GetRequiredService<ITransporteCorreoVerificacion>();
        Assert.Equal(EstadoEntregaCorreo.RechazadaDefinitivamente,(await c.EnviarAsync(Mensaje(),default)).Estado);
        mode=1;Assert.Equal(EstadoEntregaCorreo.ResultadoIncierto,(await c.EnviarAsync(Mensaje(),default)).Estado);
        Assert.DoesNotContain(logs.Text,s=>new[]{"PRIVADO","synthetic-key-not-a-secret","destino@example.invalid",new string('A',43)}.Any(s.Contains));
        Assert.Equal(2,fake.Calls);
    }

    private sealed class CaptureLogs : ILoggerProvider
    {
        public List<string> Text {get;}=[];
        public ILogger CreateLogger(string categoryName)=>new EntryLogger(Text);
        public void Dispose(){}
        private sealed class EntryLogger(List<string> entries):ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
            public bool IsEnabled(LogLevel level)=>true;
            public void Log<TState>(LogLevel level,EventId id,TState state,Exception? ex,Func<TState,Exception?,string> formatter)
                =>entries.Add(formatter(state,ex)+(ex?.ToString()??""));
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly List<Timer> timers=[];
        public override DateTimeOffset GetUtcNow()=>DateTimeOffset.Parse("2026-10-21T00:00:00Z");
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan due,TimeSpan period)
        {var t=new Timer(callback,state);timers.Add(t);return t;}
        public void Fire(){foreach(var t in timers.ToArray())t.Fire();}
        private sealed class Timer(TimerCallback callback,object? state):ITimer
        {
            private bool disposed;
            public void Fire(){if(!disposed)callback(state);}
            public bool Change(TimeSpan due,TimeSpan period)=>!disposed;
            public void Dispose()=>disposed=true;
            public ValueTask DisposeAsync(){Dispose();return ValueTask.CompletedTask;}
        }
    }
    private sealed class BlockingStream(ManualClock clock):CountingStream(1)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        {clock.Fire();await Task.Delay(Timeout.Infinite,ct);return 0;}
    }
    private class CountingStream(int length):Stream
    {
        public int BytesRead;
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        {ct.ThrowIfCancellationRequested();var n=Math.Min(buffer.Length,length-BytesRead);buffer.Span[..n].Fill((byte)'x');BytesRead+=n;return ValueTask.FromResult(n);}
        public override int Read(byte[] b,int o,int c)=>ReadAsync(b.AsMemory(o,c)).Result;
        public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();
        public override void SetLength(long l)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
}
