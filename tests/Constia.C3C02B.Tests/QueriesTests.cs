using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Constia.Application.Correo;
using Constia.Domain;
using Constia.Infrastructure.Bienvenida;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Constia.C3C02B.Tests;

[Collection("Sandbox")]
public sealed class QueriesTests
{

    [Fact,Trait("Category","Operational")]
    public async Task BudgetQueriesHandleTwentyExcessAndFutureReservations()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();await Install(f);
        var reservations=new List<DateTimeOffset>();
        for(var k=0;k<4;k++)reservations.AddRange(await SeedEightStates(f));
        await using var d=f.Db("migrator");var now=await DisposableSqlDatabase.Now(d);
        var before=await f.Snapshot();
        var twenty=Assert.Single(await Read(f,"Q10.1",now));
        Assert.Equal(20,N(twenty,"ReservasConsumidas"));Assert.Equal(0,N(twenty,"PresupuestoRestante"));
        Assert.Equal(reservations.Min().AddHours(24),twenty["ProximaCapacidadTeoricaUtc"]);
        Assert.Equal(before,await f.Snapshot());
        var previous=await d.Set<EventoBienvenida>().Select(x=>x.Id).ToArrayAsync();
        var extra=await SeedEightStates(f);
        var added=await d.Set<EventoBienvenida>().Where(x=>!previous.Contains(x.Id)).Select(x=>x.Id).ToArrayAsync();
        foreach(var id in added)await d.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET CapturadoEnUtc=DATEADD(hour,1,CapturadoEnUtc),
                ConfirmadoEnUtc=DATEADD(hour,1,ConfirmadoEnUtc),ExpiraEnUtc=DATEADD(hour,1,ExpiraEnUtc),
                ProximaEjecucionEnUtc=DATEADD(hour,1,ProximaEjecucionEnUtc),
                PrimeraReservaEnUtc=DATEADD(hour,1,PrimeraReservaEnUtc),HorizonteHttpEnUtc=DATEADD(hour,1,HorizonteHttpEnUtc),
                LeaseHastaUtc=DATEADD(hour,1,LeaseHastaUtc),UltimoLeaseHastaUtc=DATEADD(hour,1,UltimoLeaseHastaUtc),
                FinalizadoEnUtc=DATEADD(hour,1,FinalizadoEnUtc) WHERE Id={id};
            UPDATE dbo.IntentoBienvenida SET ReservadoEnUtc=DATEADD(hour,1,ReservadoEnUtc),
                FinalizadoEnUtc=DATEADD(hour,1,FinalizadoEnUtc) WHERE EventoId={id};
            """);
        reservations.AddRange(extra.Select(x=>x.AddHours(1)));
        before=await f.Snapshot();
        Assert.Equal(25,N(Assert.Single(await Read(f,"Q09.1",now)),"ReservasUltimas24Horas"));
        var over=Assert.Single(await Read(f,"Q10.1",now));
        Assert.Equal(25,N(over,"ReservasConsumidas"));Assert.Equal(0,N(over,"PresupuestoRestante"));
        Assert.Equal(reservations.OrderDescending().Take(20).Last().AddHours(24),over["ProximaCapacidadTeoricaUtc"]);
        Assert.Equal(before,await f.Snapshot());
    }

    private static async Task<(string Hash,double Cost,string[] Operators)> Plan(DisposableSqlDatabase f,string id,DateTimeOffset now)
    {
        await using var c=f.Identity.Connection("migrator",f.Identity.Database);
        await c.OpenAsync();await f.Identity.Verify(c,f.Identity.Database);
        using var toggle=c.CreateCommand();toggle.CommandText="SET SHOWPLAN_XML ON";await toggle.ExecuteNonQueryAsync();
        Exception? primary=null;
        try {
            using var cmd=c.CreateCommand();cmd.CommandTimeout=2;
            cmd.CommandText=Blocks()[id];
            cmd.Parameters.Add("@BaseEsperada",SqlDbType.NVarChar,128).Value=f.Identity.Database;
            cmd.Parameters.Add("@AhoraUtc",SqlDbType.DateTimeOffset).Value=now;cmd.Parameters["@AhoraUtc"].Scale=7;
            cmd.Parameters.Add("@Lote",SqlDbType.Int).Value=100;
            cmd.Parameters.Add("@VentanaMinutos",SqlDbType.Int).Value=60;
            cmd.Parameters.Add("@HuellaPerfil",SqlDbType.VarChar,64).Value=DisposableSqlDatabase.Options.Perfil!.Huella;
            cmd.Parameters.Add("@EpocaEsperada",SqlDbType.UniqueIdentifier).Value=DisposableSqlDatabase.Epoch;
            var xml=(string)(await cmd.ExecuteScalarAsync())!;
            var doc=System.Xml.Linq.XDocument.Parse(xml);
            var costs=doc.Descendants().Attributes().Where(x=>x.Name.LocalName=="StatementSubTreeCost")
                .Select(x=>double.Parse(x.Value,CultureInfo.InvariantCulture)).ToArray();
            Assert.NotEmpty(costs);Assert.All(costs,x=>Assert.True(double.IsFinite(x)&&x>=0));
            var operators=doc.Descendants().Where(x=>x.Name.LocalName=="RelOp")
                .Select(x=>(string?)x.Attribute("PhysicalOp")??"Unknown").Distinct().Order().ToArray();
            return (Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(xml))),costs.Max(),operators);
        } catch(Exception e) {primary=e;throw;}
        finally {
            toggle.CommandText="SET SHOWPLAN_XML OFF";
            try {await toggle.ExecuteNonQueryAsync();}
            catch when(primary is not null) { /* Preserve original failure; non-pooled session is disposed. */ }
        }
    }
    public static Dictionary<string,string> Blocks()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"queries.sql");
        if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))!="4FB171D55A27B2E62089E8EEB96D1F1FC0FFC0E4919A1AC8B2EF6A3304FFD1FD")
            throw new InvalidOperationException("Query source digest differs.");
        var text=File.ReadAllText(path);
        var parts=Regex.Split(text,@"(?m)^-- (Q\d\d\.\d+)\r?$");
        var blocks=new Dictionary<string,string>();
        for(var k=1;k<parts.Length;k+=2)blocks.Add(parts[k],parts[k+1].Trim());
        if(blocks.Count!=22 || blocks.Keys.Count(x=>x.StartsWith("Q08."))!=2 ||
            blocks.Keys.Count(x=>x.StartsWith("Q12."))!=7 ||
            blocks.Values.Any(x=>Regex.IsMatch(x,@"\b(INSERT|UPDATE|DELETE|MERGE|EXEC|DROP|ALTER|CREATE|NOLOCK|READPAST)\b",RegexOptions.IgnoreCase)))
            throw new InvalidOperationException("Approved query inventory differs.");
        return blocks;
    }
    private static async Task Install(DisposableSqlDatabase f)
    {
        // Only fixed reviewed SELECT bodies. The caller cannot supply SQL or return sensitive columns.
        await using var c=f.Identity.Connection("migrator",f.Identity.Database);
        await c.OpenAsync();await f.Identity.Verify(c,f.Identity.Database);
        foreach(var (id,sql) in Blocks())
        {
            using var cmd=c.CreateCommand();
            cmd.CommandText="CREATE PROCEDURE dbo.C3C02B_"+id.Replace('.','_')+" "+"""
                @BaseEsperada sysname,@AhoraUtc datetimeoffset(7),@Lote int,@VentanaMinutos int,
                @HuellaPerfil varchar(64),@EpocaEsperada uniqueidentifier
                WITH EXECUTE AS OWNER AS
                """+sql;
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText="GRANT EXECUTE ON dbo.C3C02B_"+id.Replace('.','_')+" TO c3_observer";
            await cmd.ExecuteNonQueryAsync();
        }
    }
    internal static async Task<List<Dictionary<string,object?>>> Read(DisposableSqlDatabase f,string id,
        DateTimeOffset now,int lot=100)
    {
        if(lot is <1 or >100 || now.Offset!=TimeSpan.Zero || !Blocks().ContainsKey(id))
            throw new ArgumentException("Invalid observation.");
        await using var c=f.Identity.Connection("observer",f.Identity.Database);
        await c.OpenAsync();await f.Identity.Verify(c,f.Identity.Database);
        using var cmd=c.CreateCommand();cmd.CommandTimeout=2;cmd.CommandType=CommandType.StoredProcedure;
        cmd.CommandText="dbo.C3C02B_"+id.Replace('.','_');
        cmd.Parameters.Add("@BaseEsperada",SqlDbType.NVarChar,128).Value=f.Identity.Database;
        cmd.Parameters.Add("@AhoraUtc",SqlDbType.DateTimeOffset).Value=now;cmd.Parameters["@AhoraUtc"].Scale=7;
        cmd.Parameters.Add("@Lote",SqlDbType.Int).Value=lot;
        cmd.Parameters.Add("@VentanaMinutos",SqlDbType.Int).Value=60;
        cmd.Parameters.Add("@HuellaPerfil",SqlDbType.VarChar,64).Value=DisposableSqlDatabase.Options.Perfil!.Huella;
        cmd.Parameters.Add("@EpocaEsperada",SqlDbType.UniqueIdentifier).Value=DisposableSqlDatabase.Epoch;
        var rows=new List<Dictionary<string,object?>>();
        using var reader=await cmd.ExecuteReaderAsync();
        while(await reader.ReadAsync())
        {
            var row=new Dictionary<string,object?>();
            for(var k=0;k<reader.FieldCount;k++)row.Add(reader.GetName(k),reader.IsDBNull(k)?null:reader.GetValue(k));
            rows.Add(row);
        }
        return rows;
    }
    private static long N(Dictionary<string,object?> row,string key)=>Convert.ToInt64(row[key],CultureInfo.InvariantCulture);
    private static async Task<List<DateTimeOffset>> SeedEightStates(DisposableSqlDatabase f)
    {
        var reservations=new List<DateTimeOffset>();
        for(var state=1;state<=8;state++)
        {
            var id=await f.Seed();
            await using var d=f.Db("migrator");
            var e=await f.Event(id);var n=await DisposableSqlDatabase.Now(d);
            var captured=n.AddMinutes(-30);var reserved=n.AddMinutes(-20);
            var hasReserve=state is 3 or 4 or 6 or 7 or 8;var activeLease=state is 2 or 3;
            Guid? owner=activeLease?Guid.NewGuid():null;
            var generation=state==1?0L:1L;
            DateTimeOffset? until=activeLease?n.AddSeconds(90):null;
            DateTimeOffset? last=state==1?null:activeLease?until:reserved.AddSeconds(90);
            DateTimeOffset? first=hasReserve?reserved:null;
            DateTimeOffset? horizon=hasReserve?reserved.AddHours(23):null;
            DateTimeOffset? final=state>=6?n.AddMinutes(-15):null;
            DateTimeOffset? next=state>=6?null:state==4?n.AddMinutes(2):n.AddMinutes(-1);
            var payload=state==1?null:new PreparadorPayloadBienvenida(DisposableSqlDatabase.Options.Perfil!).Preparar(e);
            var hash=payload is null?null:SHA256.HashData(payload);
            var profile=payload is null?null:state==5?new string('f',64):DisposableSqlDatabase.Options.Perfil!.Huella;
            var motive=state is 7 or 8?(int)MotivoFinalBienvenida.Rechazo:0;
            var provider=state==6?"synthetic-provider-id":null;
            await d.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.EventoBienvenida SET Estado={state},CapturadoEnUtc={captured},ConfirmadoEnUtc={captured},
                  ExpiraEnUtc={captured.AddHours(24)},Reservas={(hasReserve?1:0)},PrimeraReservaEnUtc={first},
                  HorizonteHttpEnUtc={horizon},LeaseId={owner},LeaseGeneracion={generation},LeaseHastaUtc={until},
                  UltimoLeaseHastaUtc={last},FinalizadoEnUtc={final},ProximaEjecucionEnUtc={next},
                  PayloadUtf8={payload},PayloadSha256={hash},PerfilProveedorVersion={profile},
                  HuboIncertidumbre={state==8},MotivoFinal={motive},IdProveedor={provider}
                WHERE Id={id}
                """);
            if(!hasReserve)continue;
            reservations.Add(reserved);
            var attemptState=state switch{3=>1,4=>4,6=>2,7=>3,_=>6};
            DateTimeOffset? attemptFinal=state==3?null:final??n.AddMinutes(-15);
            bool? initiated=state==3?null:true;
            int? delivery=state switch {3=>null,4=>(int)MotivoEntregaCorreo.LimiteVelocidad,
                7=>(int)MotivoEntregaCorreo.SolicitudRechazada,8=>(int)MotivoEntregaCorreo.RespuestaMalformada,_=>0};
            int? status=state switch {3=>null,4=>429,7=>422,_=>200};
            await d.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT dbo.IntentoBienvenida(Id,EventoId,Ordinal,ReservadoEnUtc,PropietarioLeaseId,LeaseGeneracion,
                    Estado,HttpIniciadoConfirmado,MotivoEntrega,HttpStatus,FinalizadoEnUtc)
                VALUES({Guid.NewGuid()},{id},1,{reserved},{owner??Guid.NewGuid()},1,
                    {attemptState},{initiated},{delivery},{status},{attemptFinal})
                """);
        }
        return reservations;
    }
    [Fact,Trait("Category","Pure")] public void QueryInventoryHas22ImmutableReadOnlyBlocks()
    {
        var blocks=Blocks();Assert.Equal(22,blocks.Count);
        Assert.Equal("4FB171D55A27B2E62089E8EEB96D1F1FC0FFC0E4919A1AC8B2EF6A3304FFD1FD",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"queries.sql")))));
        Assert.Contains("@HuellaPerfil",blocks["Q14.1"]);
        Assert.Contains("TOP (@Lote)",blocks["Q13.1"]);
        Assert.Contains("UltimasVeinte",blocks["Q10.1"]);
    }
    [Fact,Trait("Category","Operational")]
    public async Task All22QueriesHaveExpectedResultsAndDoNotMutateData()
    {
        await using var f=new DisposableSqlDatabase();await f.Initialize();await SeedEightStates(f);await Install(f);
        var before=await f.Snapshot();
        await using var d=f.Db("migrator");var now=await DisposableSqlDatabase.Now(d);
        var results=new Dictionary<string,List<Dictionary<string,object?>>>();
        var evidence=new List<object>();
        foreach(var id in Blocks().Keys)
        {
            var sw=Stopwatch.StartNew();results[id]=await Read(f,id,now);sw.Stop();
            var plan=await Plan(f,id,now);
            evidence.Add(new{id,rows=results[id].Count,elapsed_ms=sw.Elapsed.TotalMilliseconds,syntax=true,restricted_role=true,
                plan_sha256=plan.Hash,estimated_cost=plan.Cost,operators=plan.Operators});
        }
        var q=results;
        Assert.Equal(1,N(Assert.Single(q["Q00.1"]),"EsBaseEsperada"));Assert.Equal(16,N(q["Q00.1"][0],"MajorVersion"));
        Assert.Equal(Enumerable.Range(1,8).Select(x=>(long)x),q["Q01.1"].Select(x=>N(x,"Estado")));
        Assert.All(q["Q01.1"],x=>Assert.Equal(1,N(x,"Cantidad")));
        Assert.Equal(1,N(Assert.Single(q["Q02.1"]),"EsperandoReintento"));
        Assert.Equal(0,N(q["Q02.1"][0],"FechaProgramadaAlcanzada"));
        Assert.Equal(new long[]{2,3},q["Q03.1"].Select(x=>N(x,"Estado")));Assert.Empty(q["Q04.1"]);
        Assert.Equal(0,N(Assert.Single(q["Q05.1"]),"EventosConLimiteProximo"));
        Assert.Equal(1,N(Assert.Single(q["Q06.1"]),"AceptadosPorProveedor"));
        Assert.Equal(1,N(Assert.Single(q["Q07.1"]),"Cantidad"));
        Assert.Equal((int)MotivoFinalBienvenida.Rechazo,N(q["Q07.1"][0],"MotivoFinal"));
        Assert.Equal(1,N(Assert.Single(q["Q08.1"]),"FinalizadosInciertos"));
        Assert.Equal(8,N(Assert.Single(q["Q08.2"]),"Estado"));
        Assert.Equal(5,N(Assert.Single(q["Q09.1"]),"ReservasUltimas24Horas"));
        Assert.Equal(15,N(Assert.Single(q["Q10.1"]),"PresupuestoRestante"));
        Assert.Null(q["Q10.1"][0]["ProximaCapacidadTeoricaUtc"]);
        Assert.Equal(1,N(Assert.Single(q["Q11.1"]),"CoincideEpocaEsperada"));
        Assert.Equal(true,q["Q11.1"][0]["CapturaHabilitada"]);Assert.Equal(true,q["Q11.1"][0]["EnvioHabilitado"]);
        Assert.Equal(false,q["Q11.1"][0]["PausaProveedor"]);Assert.Equal(1,N(q["Q11.1"][0],"VersionEsquema"));
        Assert.All(Assert.Single(q["Q12.1"]).Values,x=>Assert.NotNull(x));
        Assert.Equal("20261023003711_AddWelcomeEmailOutbox",Assert.Single(q["Q12.2"])["MigrationId"]);
        CheckMetadata(d,q);
        Assert.Equal(8,q["Q12.7"].Count);Assert.All(q["Q12.7"],x=>Assert.Equal("Vigente",x["GrupoEpoca"]));
        Assert.Equal(3,N(Assert.Single(q["Q13.1"]),"EventosObservados"));
        Assert.Equal(3,N(q["Q13.1"][0],"FinalesPorDepurarObservados"));
        Assert.Equal(0,N(q["Q13.1"][0],"LimiteAlcanzado"));
        Assert.Equal(5,N(Assert.Single(q["Q14.1"]),"Estado"));
        Assert.Equal(before,await f.Snapshot());
        // Boundary sets: active versus expired leases, bounded lower bound, future timestamps in the rolling window.
        await d.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET LeaseHastaUtc={now} WHERE Estado IN (2,3);
            """);
        var boundarySnapshot=await f.Snapshot();
        var expired=await Read(f,"Q04.1",now);Assert.Equal(new long[]{2,3},expired.Select(x=>N(x,"Estado")));
        Assert.Empty(await Read(f,"Q03.1",now));
        Assert.Equal(1,N(Assert.Single(await Read(f,"Q13.1",now,1)),"LimiteAlcanzado"));
        Assert.Equal(boundarySnapshot,await f.Snapshot());
        // Nonzero Q05 and exact Q02 eligibility boundary, prepared only after the immutable-read proof.
        var pending=await d.Set<EventoBienvenida>().AsNoTracking().SingleAsync(x=>x.Estado==EstadoEventoBienvenida.Pendiente);
        var near=now.AddMinutes(30);var captured=near.AddHours(-24);
        await d.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EventoBienvenida SET CapturadoEnUtc={captured},ConfirmadoEnUtc={captured},ExpiraEnUtc={near}
                WHERE Id={pending.Id};
            UPDATE dbo.EventoBienvenida SET ProximaEjecucionEnUtc={now} WHERE Estado=4;
            """);
        boundarySnapshot=await f.Snapshot();
        Assert.Equal(1,N(Assert.Single(await Read(f,"Q05.1",now)),"EventosConLimiteProximo"));
        Assert.Equal(1,N(Assert.Single(await Read(f,"Q02.1",now)),"FechaProgramadaAlcanzada"));
        Assert.Equal(boundarySnapshot,await f.Snapshot());
        Directory.CreateDirectory("/run/c3/query-evidence");
        await File.WriteAllTextAsync("/run/c3/query-evidence/blocks.json",JsonSerializer.Serialize(evidence));
    }
    private static void CheckMetadata(Constia.Infrastructure.ConstiaDbContext d,
        Dictionary<string,List<Dictionary<string,object?>>> q)
    {
        var model=d.GetService<IDesignTimeModel>().Model;
        string Normalize(string sql) {
            var expanded=Regex.Replace(sql,@"(\[\w+\]|\w+)\s+BETWEEN\s+(\d+)\s+AND\s+(\d+)","$1 >= $2 AND $1 <= $3",RegexOptions.IgnoreCase);
            return Regex.Replace(expanded,@"[\s\[\]()]","").ToUpperInvariant();
        }
        foreach(var type in new[]{typeof(EventoBienvenida),typeof(IntentoBienvenida),typeof(ControlBienvenida)})
        {
            var entity=model.FindEntityType(type)!;var table=entity.GetTableName()!;
            var store=StoreObjectIdentifier.Table(table,entity.GetSchema());
            var actual=q["Q12.3"].Where(x=>Equals(x["Tabla"],table)).ToArray();
            Assert.Equal(entity.GetProperties().Count(),actual.Length);
            foreach(var property in entity.GetProperties())
            {
                var row=Assert.Single(actual,x=>Equals(x["Columna"],property.GetColumnName(store)));
                Assert.Equal(property.IsNullable,row["is_nullable"]);
                var sql=property.GetColumnType()!;
                var expected=sql.Split('(')[0]=="rowversion"?"timestamp":sql.Split('(')[0];
                Assert.Equal(expected,row["Tipo"]);
                if(sql.Contains("(max)"))Assert.Equal(-1,N(row,"LongitudBytes"));
                else if(Regex.Match(sql,@"^(n?varchar|varbinary|binary)\((\d+)\)$") is {Success:true} sized) {
                    var bytes=int.Parse(sized.Groups[2].Value,CultureInfo.InvariantCulture)*(sized.Groups[1].Value=="nvarchar"?2:1);
                    Assert.Equal(bytes,N(row,"LongitudBytes"));
                }
                if(sql.StartsWith("datetimeoffset"))Assert.Equal(7,N(row,"scale"));
                Assert.Equal(false,row["is_identity"]);
            }
            var checks=entity.GetCheckConstraints().ToDictionary(x=>x.Name!,x=>x.Sql);
            var actualChecks=q["Q12.4"].Where(x=>Equals(x["Tabla"],table)).ToArray();
            Assert.Equal(checks.Keys.Order(),actualChecks.Select(x=>(string)x["name"]!).Order());
            foreach(var check in actualChecks)Assert.Equal(Normalize(checks[(string)check["name"]!]),Normalize((string)check["definition"]!));
            var indices=entity.GetIndexes().ToDictionary(x=>x.GetDatabaseName()!,x=>x.Properties.Select(p=>p.GetColumnName(store)!).ToArray());
            foreach(var key in entity.GetKeys())indices.Add(key.GetName()!,key.Properties.Select(p=>p.GetColumnName(store)!).ToArray());
            var actualIndices=q["Q12.5"].Where(x=>Equals(x["Tabla"],table)).GroupBy(x=>(string)x["Indice"]!).ToArray();
            Assert.Equal(indices.Keys.Order(),actualIndices.Select(x=>x.Key).Order());
            foreach(var index in actualIndices) {
                Assert.Equal(indices[index.Key],index.Where(x=>N(x,"key_ordinal")>0).OrderBy(x=>N(x,"key_ordinal")).Select(x=>(string)x["Columna"]!));
                var configured=entity.GetIndexes().SingleOrDefault(x=>x.GetDatabaseName()==index.Key);
                Assert.All(index,x=>{
                    Assert.Equal(configured?.IsUnique??true,x["is_unique"]);
                    Assert.Equal(entity.FindPrimaryKey()?.GetName()==index.Key,x["is_primary_key"]);
                });
                Assert.All(index,x=>{Assert.Equal(false,x["is_disabled"]);Assert.Equal(false,x["is_descending_key"]);Assert.Null(x["filter_definition"]);});
            }
        }
        Assert.NotEmpty(q["Q12.4"]);Assert.All(q["Q12.4"],x=>{
            Assert.Equal(false,x["is_disabled"]);Assert.Equal(false,x["is_not_trusted"]);Assert.NotNull(x["definition"]);
        });
        Assert.Contains(q["Q12.5"],x=>Equals(x["Indice"],"UX_EventoBienvenida_UsuarioId") && Equals(x["is_unique"],true));
        Assert.Contains(q["Q12.5"],x=>Equals(x["Indice"],"UX_IntentoBienvenida_EventoId_Ordinal") && Equals(x["is_unique"],true));
        Assert.Equal(2,q["Q12.6"].Count);Assert.All(q["Q12.6"],x=>{
            Assert.Equal(false,x["is_disabled"]);Assert.Equal(false,x["is_not_trusted"]);
            Assert.Equal("NO_ACTION",x["delete_referential_action_desc"]);
        });
        Assert.Contains(q["Q12.6"],x=>Equals(x["TablaOrigen"],"EventoBienvenida") && Equals(x["ColumnaOrigen"],"UsuarioId") && Equals(x["TablaDestino"],"Usuario") && Equals(x["ColumnaDestino"],"Id"));
        Assert.Contains(q["Q12.6"],x=>Equals(x["TablaOrigen"],"IntentoBienvenida") && Equals(x["ColumnaOrigen"],"EventoId") && Equals(x["TablaDestino"],"EventoBienvenida") && Equals(x["ColumnaDestino"],"Id"));
    }
}
