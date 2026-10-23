using System.Text.Json;
using Constia.Application.Bienvenida;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure.Configurations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
namespace Constia.Infrastructure.Bienvenida;
internal sealed class CompatibilidadEsquemaBienvenida(ConstiaDbContext db)
{
    public async Task<bool> EsCompatibleAsync(CancellationToken ct)
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
