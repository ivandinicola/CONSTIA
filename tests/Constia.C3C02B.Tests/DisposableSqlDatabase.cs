using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Constia.C3C02B.Harness;
using Constia.Application.Bienvenida;
using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Bienvenida;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Constia.C3C02B.Tests;

// Sequential exclusive database lifetime. No local opt-in, persistent target, or guard bypass.
[CollectionDefinition("Sandbox", DisableParallelization=true)]
public sealed class SandboxCollection;

public sealed class DisposableSqlDatabase : IAsyncDisposable
{
    public SandboxIdentity Identity { get; } = SandboxIdentity.Load();
    public static readonly Guid Epoch=Guid.Parse("77777777-7777-4777-8777-777777777777");
    public static OpcionesProcesamientoBienvenida Options => OpcionesProcesamientoBienvenida.DesdeValores(
        "true","true","true","synthetic-account","correo@example.invalid");
    private bool owns;
    public ConstiaDbContext Db(string role="runtime") => new(new DbContextOptionsBuilder<ConstiaDbContext>()
        .UseSqlServer(Identity.Connection(role,Identity.Database).ConnectionString)
        .AddInterceptors(new IdentityConnectionInterceptor(Identity)).Options);
    public OutboxBienvenidaRepository Repo(ConstiaDbContext db)=>new(db,Options);
    public async Task Initialize()
    {
        await using var master=Identity.Connection("admin","master");
        await master.OpenAsync(); await Identity.Verify(master,"master");
        using(var absent=master.CreateCommand())
        {
            absent.CommandText="SELECT COUNT(*) FROM sys.databases WHERE name=@name";
            absent.Parameters.Add("@name",SqlDbType.NVarChar,128).Value=Identity.Database;
            if (Convert.ToInt32(await absent.ExecuteScalarAsync())!=0) throw new InvalidOperationException("Database already exists; refusing reuse.");
        }
        // Identifier is exclusively the validated fixed prefix + 32 hex nonce.
        using(var create=master.CreateCommand())
        {
            create.CommandText="CREATE DATABASE ["+Identity.Database+"]";
            await create.ExecuteNonQueryAsync(); owns=true;
        }
        var keys=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText("/run/c3/credentials.json"))!;
        foreach(var role in new[]{"runtime","observer","migrator"})
        {
            var password=keys[role];
            if (!System.Text.RegularExpressions.Regex.IsMatch(password,"^[0-9a-f]{64}aA!7$")) throw new InvalidOperationException("Bad synthetic credential.");
            using var login=master.CreateCommand();
            // DDL passwords/identifiers cannot be SQL parameters. Both have strict fixed/hex domains.
            login.CommandText="IF SUSER_ID(N'c3_"+role+"') IS NULL CREATE LOGIN [c3_"+role+"] WITH PASSWORD=N'"+password+"',CHECK_POLICY=OFF;";
            await login.ExecuteNonQueryAsync();
        }
        await using(var admin=Identity.Connection("admin",Identity.Database))
        {
            await admin.OpenAsync(); await Identity.Verify(admin,Identity.Database);
            foreach(var role in new[]{"runtime","observer","migrator"})
            {
                using var user=admin.CreateCommand();
                user.CommandText="CREATE USER [c3_"+role+"] FOR LOGIN [c3_"+role+"];";
                if(role=="migrator") user.CommandText+=" GRANT CONTROL ON DATABASE::["+Identity.Database+"] TO [c3_migrator];";
                await user.ExecuteNonQueryAsync();
            }
        }
        await using(var migrate=Db("migrator")) await migrate.Database.MigrateAsync();
        await using(var admin=Identity.Connection("admin",Identity.Database))
        {
            await admin.OpenAsync(); await Identity.Verify(admin,Identity.Database);
            using var grant=admin.CreateCommand();
            grant.CommandText="""
                GRANT SELECT,INSERT,UPDATE,DELETE ON dbo.EventoBienvenida TO c3_runtime;
                GRANT SELECT,INSERT,UPDATE,DELETE ON dbo.IntentoBienvenida TO c3_runtime;
                GRANT SELECT,UPDATE ON dbo.ControlBienvenida TO c3_runtime;
                GRANT SELECT ON dbo.__EFMigrationsHistory TO c3_runtime;
                GRANT SELECT,UPDATE ON dbo.Usuario TO c3_runtime;
                GRANT SELECT,UPDATE ON dbo.DesafioVerificacionCorreo TO c3_runtime;
                GRANT VIEW DEFINITION ON dbo.EventoBienvenida TO c3_runtime;
                GRANT VIEW DEFINITION ON dbo.IntentoBienvenida TO c3_runtime;
                GRANT VIEW DEFINITION ON dbo.ControlBienvenida TO c3_runtime;
                GRANT VIEW DEFINITION ON dbo.EventoBienvenida TO c3_observer;
                GRANT VIEW DEFINITION ON dbo.IntentoBienvenida TO c3_observer;
                GRANT VIEW DEFINITION ON dbo.ControlBienvenida TO c3_observer;
                GRANT VIEW DEFINITION ON dbo.__EFMigrationsHistory TO c3_observer;
                GRANT SELECT ON dbo.__EFMigrationsHistory TO c3_observer;
                GRANT SELECT ON dbo.ControlBienvenida TO c3_observer;
                GRANT SELECT (Id,Estado,EpocaCaptura,Tipo,PlantillaVersion,PerfilProveedorVersion,
                    ConfirmadoEnUtc,CapturadoEnUtc,ExpiraEnUtc,ProximaEjecucionEnUtc,
                    PrimeraReservaEnUtc,HorizonteHttpEnUtc,Reservas,LeaseId,LeaseGeneracion,LeaseHastaUtc,
                    UltimoLeaseHastaUtc,HuboIncertidumbre,MotivoFinal,FinalizadoEnUtc,DatosDepuradosEnUtc)
                    ON dbo.EventoBienvenida TO c3_observer;
                GRANT SELECT (Id,EventoId,Ordinal,ReservadoEnUtc,Estado,FinalizadoEnUtc)
                    ON dbo.IntentoBienvenida TO c3_observer;
                """;
            await grant.ExecuteNonQueryAsync();
        }
        await using var db=Db("migrator");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.ControlBienvenida SET CapturaHabilitada=1,EpocaCaptura={Epoch},
                InicioCapturaEnUtc=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00'),EnvioHabilitado=1 WHERE Id=1
            """);
    }
    public async Task<Guid> Seed()
    {
        await using var d=Db("migrator");
        var n=await Now(d);
        var u=new Usuario("Synthetic",Guid.NewGuid().ToString("N")+"@example.invalid","synthetic-hash","Etc/UTC");
        d.Add(u); await d.SaveChangesAsync();
        await using var tx=await d.Database.BeginTransactionAsync();
        u.ConfirmarCorreo(n);
        var e=EventoBienvenida.CrearPendiente(u.Id,Epoch,u.Email,n,n);
        d.Add(e); await d.SaveChangesAsync(); await tx.CommitAsync();
        await using var check=Db("migrator");
        var stored=await check.Set<EventoBienvenida>().AsNoTracking().SingleAsync(x=>x.Id==e.Id);
        var user=await check.Set<Usuario>().AsNoTracking().SingleAsync(x=>x.Id==u.Id);
        if(user.EstadoVerificacionCorreo!=EstadoVerificacionCorreo.Verificado ||
            stored.Estado!=EstadoEventoBienvenida.Pendiente || stored.Reservas!=0 ||
            stored.UsuarioId!=u.Id || stored.Destinatario!=u.Email ||
            stored.EpocaCaptura!=Epoch || stored.ExpiraEnUtc!=n.AddHours(24) ||
            await check.Set<IntentoBienvenida>().AnyAsync(x=>x.EventoId==e.Id))
            throw new InvalidOperationException("Invalid synthetic fixture.");
        return e.Id;
    }
    public static Task<DateTimeOffset> Now(ConstiaDbContext d)=>d.Database.SqlQueryRaw<DateTimeOffset>(
        "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00') AS [Value]").SingleAsync();
    public async Task<EventoBienvenida> Event(Guid id)
    {await using var d=Db("migrator"); return await d.Set<EventoBienvenida>().AsNoTracking().SingleAsync(x=>x.Id==id);}
    public async Task<string> Snapshot()
    {
        await using var c=Identity.Connection("migrator",Identity.Database);
        await c.OpenAsync(); await Identity.Verify(c,Identity.Database);
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var table in new[]{"Usuario","EventoBienvenida","IntentoBienvenida","ControlBienvenida","__EFMigrationsHistory"})
        {
            using var cmd=c.CreateCommand(); cmd.CommandText="SELECT * FROM dbo.["+table+"] ORDER BY 1";
            using var r=await cmd.ExecuteReaderAsync();
            while(await r.ReadAsync()) for(var k=0;k<r.FieldCount;k++)
            {
                var value=r.GetValue(k);
                var bytes=JsonSerializer.SerializeToUtf8Bytes(value);
                hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public async ValueTask DisposeAsync()
    {
        if(!owns)return;
        await using var c=Identity.Connection("admin","master");
        await c.OpenAsync(); await Identity.Verify(c,"master");
        using var cmd=c.CreateCommand();
        cmd.CommandText="ALTER DATABASE ["+Identity.Database+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+Identity.Database+"];";
        await cmd.ExecuteNonQueryAsync(); owns=false;
    }
}
