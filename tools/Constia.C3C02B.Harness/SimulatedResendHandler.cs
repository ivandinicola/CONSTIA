using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Constia.C3C02B.Harness;

/// <summary>There is deliberately no InnerHandler and no socket-backed fallback.</summary>
public sealed class SimulatedResendHandler(PhaseGate gate, string response = "accepted") : HttpMessageHandler
{
    public const string Key = "c3c02b-synthetic-not-a-provider-key";
    public int Calls { get; private set; }
    public List<(string Payload, string Key)> Fingerprints { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri?.AbsoluteUri != "https://api.resend.com/emails" ||
            request.Headers.Authorization?.Parameter != Key)
            throw new InvalidOperationException("Synthetic transport precondition.");
        var bytes = await request.Content!.ReadAsByteArrayAsync(ct);
        using var doc = JsonDocument.Parse(bytes);
        var to = doc.RootElement.GetProperty("to");
        if (to.GetArrayLength() != 1 || !to[0].GetString()!.EndsWith("@example.invalid", StringComparison.Ordinal) ||
            !doc.RootElement.GetProperty("from").GetString()!.EndsWith("@example.invalid>", StringComparison.Ordinal))
            throw new InvalidOperationException("Synthetic addresses required.");
        Calls++;
        Fingerprints.Add((Convert.ToHexString(SHA256.HashData(bytes)),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Headers.GetValues("Idempotency-Key").Single())))));
        Console.WriteLine("HTTP:"+Fingerprints[^1].Payload+":"+Fingerprints[^1].Key);
        await gate.Reach("handler", ct);
        if (response == "tls")
            throw new HttpRequestException(HttpRequestError.SecureConnectionError, "Synthetic TLS failure");
        var status = response switch { "auth" => HttpStatusCode.Unauthorized, "rate" or "daily" or "monthly" => HttpStatusCode.TooManyRequests, _ => HttpStatusCode.OK };
        var body = response switch {
            "auth" => """{"name":"validation_error","message":"synthetic"}""",
            "daily" => """{"name":"daily_quota_exceeded","message":"synthetic"}""",
            "monthly" => """{"name":"monthly_quota_exceeded","message":"synthetic"}""",
            "rate" => """{"name":"rate_limit_exceeded","message":"synthetic"}""",
            _ => """{"id":"synthetic-provider-id"}"""
        };
        await gate.Reach("response", ct);
        var result = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (response == "rate") result.Headers.RetryAfter = new(TimeSpan.FromMinutes(2));
        return result;
    }
}

public sealed class PhaseGate(string? blocked = null)
{
    public static readonly string[] Names = ["started","acquired","frozen","reserved","authorized","handler","response","recorded","maintenance","sql-error","released"];
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Stop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void StartInputPump() => _ = Task.Run(async () => {
        while(await Console.In.ReadLineAsync() is { } line) {
            if(line=="continue:"+blocked) release.TrySetResult();
            else if(line=="stop") { Stop.TrySetResult(); return; }
            else { release.TrySetException(new InvalidOperationException("Unknown control input.")); Stop.TrySetResult(); return; }
        }
        release.TrySetCanceled(); Stop.TrySetResult();
    });
    public async Task Reach(string phase, CancellationToken ct)
    {
        if (!Names.Contains(phase)) throw new InvalidOperationException("Unknown phase.");
        Console.WriteLine("PHASE:" + phase);
        if (phase != blocked) return;
        await release.Task.WaitAsync(TimeSpan.FromSeconds(120),ct);
    }
}
