using System.Text.Json;
using Constia.Application.Bienvenida;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure.Configurations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
namespace Constia.Infrastructure.Bienvenida;
/// <summary>No guarda ni confirma: participa exclusivamente en la transacción de B.</summary>
internal sealed class CapturaBienvenidaEnConfirmacion(ConstiaDbContext db, OpcionesCapturaBienvenida opciones)
{
    public async Task<EventoBienvenida?> PrepararAsync(Usuario usuario, DateTimeOffset confirmadoUtc, CancellationToken ct)
    {
        if(db.Database.CurrentTransaction is null) throw new InvalidOperationException("Se requiere la transacción de confirmación.");
        if(!opciones.EsValida) throw new CorreoNoDisponibleException();
        var presencia=await db.Database.SqlQueryRaw<int>("""
            SELECT CASE WHEN
              OBJECT_ID(N'dbo.ControlBienvenida',N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.EventoBienvenida',N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.IntentoBienvenida',N'U') IS NOT NULL
              AND EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]AddWelcomeEmailOutbox') THEN 1
            WHEN OBJECT_ID(N'dbo.ControlBienvenida') IS NOT NULL
              OR OBJECT_ID(N'dbo.EventoBienvenida') IS NOT NULL
              OR OBJECT_ID(N'dbo.IntentoBienvenida') IS NOT NULL
              OR EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]AddWelcomeEmailOutbox') THEN -1
            ELSE 0 END AS [Value]
            """).SingleAsync(ct);
        if(presencia==0 && !opciones.Habilitada) return null;
        if(presencia!=1) throw new CorreoNoDisponibleException();
        // Lecturas compartidas compatibles, retenidas hasta el commit. No se confía en el tracker.
        var control=await db.Set<ControlBienvenida>().FromSqlRaw(
            "SELECT * FROM [dbo].[ControlBienvenida] WITH (HOLDLOCK) WHERE [Id]=1").AsNoTracking().SingleOrDefaultAsync(ct);
        if(control is null || control.VersionEsquema!=1 || control.ProtocoloEscritor!=1 ||
            control.CapturaHabilitada!=opciones.Habilitada) throw new CorreoNoDisponibleException();
        // Instalado y apagado también debe ser compatible; sólo la ausencia total conserva B sin C3-A.
        if(!await new CompatibilidadEsquemaBienvenida(db).EsCompatibleAsync(ct)) throw new CorreoNoDisponibleException();
        if(!control.CapturaHabilitada) return null;
        if(control.EpocaCaptura!=opciones.Epoca) throw new CorreoNoDisponibleException();
        var ahora=await db.Database.SqlQueryRaw<DateTimeOffset>(
            "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00') AS [Value]").SingleAsync(ct);
        return EventoBienvenida.CrearPendiente(usuario.Id,opciones.Epoca!.Value,usuario.Email,confirmadoUtc,ahora);
    }
}
