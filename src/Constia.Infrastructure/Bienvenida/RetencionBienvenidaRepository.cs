using System.Data;
using Constia.Application.Bienvenida;
using Constia.Domain;
using Microsoft.EntityFrameworkCore;
namespace Constia.Infrastructure.Bienvenida;
/// <summary>Mantenimiento invocable, sin scheduler, transporte ni reservas.</summary>
public sealed class RetencionBienvenidaRepository(ConstiaDbContext db) : IRetencionBienvenidaRepository
{
    public async Task<int> AplicarAsync(int maximo,CancellationToken ct)
    {
        if(maximo is <1 or >100) throw new ArgumentOutOfRangeException(nameof(maximo));
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var now=await db.Database.SqlQueryRaw<DateTimeOffset>(
            "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00') AS [Value]").SingleAsync(ct);
        var candidates=await db.Set<EventoBienvenida>().FromSqlInterpolated($"""
            SELECT TOP ({maximo}) e.* FROM dbo.EventoBienvenida e WITH (UPDLOCK,HOLDLOCK)
            WHERE
             (e.Estado=1 AND e.Reservas=0 AND e.LeaseId IS NULL AND e.ExpiraEnUtc<={now}
                AND NOT EXISTS(SELECT 1 FROM dbo.IntentoBienvenida i WHERE i.EventoId=e.Id))
             OR
             (e.Estado BETWEEN 6 AND 8 AND e.LeaseId IS NULL
                AND (e.UltimoLeaseHastaUtc IS NULL OR DATEADD(minute,10,e.UltimoLeaseHastaUtc)<={now})
                AND (e.DatosDepuradosEnUtc IS NULL OR
                    (DATEADD(day,30,e.FinalizadoEnUtc)<={now} AND
                        (e.IdProveedor IS NOT NULL OR EXISTS(SELECT 1 FROM dbo.IntentoBienvenida i
                         WHERE i.EventoId=e.Id AND DATEADD(hour,24,i.ReservadoEnUtc)<={now})))))
            ORDER BY e.Id
            """).AsNoTracking().ToArrayAsync(ct);
        var changed=0;
        foreach(var e in candidates)
        {
            var count=0;
            if(e.Estado==EstadoEventoBienvenida.Pendiente)
                count+=await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE dbo.EventoBienvenida SET Estado=7,MotivoFinal=3,FinalizadoEnUtc={now},ProximaEjecucionEnUtc=NULL
                    WHERE Id={e.Id} AND Estado=1 AND Reservas=0 AND LeaseId IS NULL AND ExpiraEnUtc<={now}
                    """,ct);
            count+=await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.EventoBienvenida SET Destinatario=NULL,PayloadUtf8=NULL,PayloadSha256=NULL,DatosDepuradosEnUtc={now}
                WHERE Id={e.Id} AND Estado BETWEEN 6 AND 8 AND LeaseId IS NULL AND DatosDepuradosEnUtc IS NULL
                AND (UltimoLeaseHastaUtc IS NULL OR DATEADD(minute,10,UltimoLeaseHastaUtc)<={now})
                """,ct);
            count+=await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM dbo.IntentoBienvenida WHERE EventoId={e.Id} AND DATEADD(hour,24,ReservadoEnUtc)<={now}
                AND EXISTS (SELECT 1 FROM dbo.EventoBienvenida e WHERE e.Id={e.Id} AND e.Estado BETWEEN 6 AND 8
                    AND e.LeaseId IS NULL AND DATEADD(day,30,e.FinalizadoEnUtc)<={now}
                    AND (e.UltimoLeaseHastaUtc IS NULL OR DATEADD(minute,10,e.UltimoLeaseHastaUtc)<={now}))
                """,ct);
            count+=await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.EventoBienvenida SET IdProveedor=NULL WHERE Id={e.Id} AND IdProveedor IS NOT NULL
                AND Estado BETWEEN 6 AND 8 AND LeaseId IS NULL AND DATEADD(day,30,FinalizadoEnUtc)<={now}
                AND (UltimoLeaseHastaUtc IS NULL OR DATEADD(minute,10,UltimoLeaseHastaUtc)<={now})
                """,ct);
            if(count>0) changed++;
        }
        await tx.CommitAsync(ct);
        return changed;
    }
}
