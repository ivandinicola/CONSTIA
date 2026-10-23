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
        if(!await EsquemaCompatibleAsync(ct)) throw new CorreoNoDisponibleException();
        if(!control.CapturaHabilitada) return null;
        if(control.EpocaCaptura!=opciones.Epoca) throw new CorreoNoDisponibleException();
        var ahora=await db.Database.SqlQueryRaw<DateTimeOffset>(
            "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00') AS [Value]").SingleAsync(ct);
        return EventoBienvenida.CrearPendiente(usuario.Id,opciones.Epoca!.Value,usuario.Email,confirmadoUtc,ahora);
    }
    private async Task<bool> EsquemaCompatibleAsync(CancellationToken ct)
    {
        var migration=db.Database.GetMigrations().Single(x=>x.EndsWith("_AddWelcomeEmailOutbox",StringComparison.Ordinal));
        var types=new[]{typeof(EventoBienvenida),typeof(IntentoBienvenida),typeof(ControlBienvenida)};
        var columns=types.SelectMany(type=>{
            var e=db.Model.FindEntityType(type)!;var table=e.GetTableName()!;
            var store=StoreObjectIdentifier.Table(table,e.GetSchema());
            return e.GetProperties().Select(p=>{
                var sql=p.GetColumnType()!;var name=sql.Split('(')[0].ToLowerInvariant();
                var id=name switch {"uniqueidentifier"=>36,"tinyint"=>48,"bigint"=>127,"int"=>56,"smallint"=>52,"bit"=>104,"datetimeoffset"=>43,"varchar"=>167,"nvarchar"=>231,"varbinary"=>165,"binary"=>173,"rowversion"=>189,_=>throw new InvalidOperationException("Tipo de outbox no soportado.")};
                var length=id is 167 or 231 or 165 or 173
                    ? (sql.Contains("(max)",StringComparison.OrdinalIgnoreCase)?-1:int.Parse(sql[(sql.IndexOf('(')+1)..sql.IndexOf(')')])*(id==231?2:1)):0;
                return new{table,column=p.GetColumnName(store),type=id,length,nullable=p.IsNullable};
            });
        }).ToArray();
        var checks=EventoBienvenidaConfiguration.Checks.Select(x=>x.Name)
            .Concat(IntentoBienvenidaConfiguration.Checks.Select(x=>x.Name))
            .Concat(ControlBienvenidaConfiguration.Checks.Select(x=>x.Name)).ToArray();
        return await db.Database.SqlQueryRaw<int>("""
            SELECT CASE WHEN
              EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@migration)
              AND NOT EXISTS (
                SELECT 1 FROM OPENJSON(@columns) WITH (
                  [table] nvarchar(128), [column] nvarchar(128), [type] int, [length] int, [nullable] bit) e
                LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+e.[table]) AND c.name=e.[column]
                WHERE c.column_id IS NULL OR c.system_type_id<>e.[type] OR c.is_nullable<>e.[nullable]
                  OR (e.[type] IN (167,231,165,173) AND c.max_length<>e.[length]))
              AND NOT EXISTS (
                SELECT 1 FROM OPENJSON(@checks) e LEFT JOIN sys.check_constraints c ON c.name=e.[value]
                AND c.parent_object_id IN (OBJECT_ID(N'dbo.EventoBienvenida'),OBJECT_ID(N'dbo.IntentoBienvenida'),OBJECT_ID(N'dbo.ControlBienvenida'))
                WHERE c.object_id IS NULL OR c.is_disabled=1 OR c.is_not_trusted=1)
              AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.EventoBienvenida') AND name=N'UX_EventoBienvenida_UsuarioId' AND is_unique=1 AND is_disabled=0 AND has_filter=0)
              AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.IntentoBienvenida') AND name=N'UX_IntentoBienvenida_EventoId_Ordinal' AND is_unique=1 AND is_disabled=0 AND has_filter=0)
              AND (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N'FK_EventoBienvenida_Usuario_UsuarioId',N'FK_IntentoBienvenida_EventoBienvenida_EventoId') AND is_disabled=0 AND is_not_trusted=0 AND delete_referential_action=0)=2
            THEN 1 ELSE 0 END AS [Value]
            """,new SqlParameter("@migration",migration),new SqlParameter("@columns",JsonSerializer.Serialize(columns)),
                new SqlParameter("@checks",JsonSerializer.Serialize(checks))).SingleAsync(ct)==1;
    }
}
