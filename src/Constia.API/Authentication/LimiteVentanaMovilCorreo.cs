using System.Threading.RateLimiting;

namespace Constia.API.Authentication;

/// <summary>Ventana móvil exacta por IP, en memoria de esta instancia; sin cola ni confianza en headers reenviados.</summary>
public sealed class LimiteVentanaMovilCorreo(int maximo, TimeSpan ventana, TimeProvider clock) : RateLimiter
{
    private readonly object gate = new();
    private readonly Queue<DateTimeOffset> solicitudes = new();
    private long aprobadas, rechazadas;
    private DateTimeOffset ultimoUso = clock.GetUtcNow();

    private void Expirar(DateTimeOffset now)
    {
        while(solicitudes.TryPeek(out var t) && t <= now.Subtract(ventana)) solicitudes.Dequeue();
    }

    public override TimeSpan? IdleDuration
    {
        get
        {
            lock(gate)
            {
                var now=clock.GetUtcNow(); Expirar(now);
                return solicitudes.Count==0 ? now-ultimoUso : null;
            }
        }
    }

    public override RateLimiterStatistics GetStatistics()
    {
        lock(gate)
        {
            Expirar(clock.GetUtcNow());
            return new RateLimiterStatistics
            {
                CurrentAvailablePermits=maximo-solicitudes.Count,CurrentQueuedCount=0,
                TotalSuccessfulLeases=aprobadas,TotalFailedLeases=rechazadas
            };
        }
    }

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        lock(gate)
        {
            var now=clock.GetUtcNow(); Expirar(now);
            if(permitCount > maximo-solicitudes.Count) { rechazadas++; return Lease.Rechazada; }
            for(var i=0;i<permitCount;i++) solicitudes.Enqueue(now);
            ultimoUso=now; aprobadas++;
            return Lease.Aprobada;
        }
    }

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(AttemptAcquireCore(permitCount));
    }

    private sealed class Lease(bool aprobada) : RateLimitLease
    {
        internal static readonly Lease Aprobada=new(true);
        internal static readonly Lease Rechazada=new(false);
        public override bool IsAcquired=>aprobada;
        public override IEnumerable<string> MetadataNames=>Array.Empty<string>();
        public override bool TryGetMetadata(string name,out object? metadata) { metadata=null; return false; }
    }
}
