using Constia.Infrastructure;
using Constia.Infrastructure.Correo;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Constia.API.Tests;

public sealed class AutorizacionEnvioUnicoSqlTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private sealed class C109SqlFact : FactAttribute
    {
        public C109SqlFact() { if (Environment.GetEnvironmentVariable("CONSTIA_C109_SQL_TESTS") != "true") Skip = "Requires explicitly authorized and identity-verified disposable C109 SQL Server."; }
    }
    [C109SqlFact]
    public async Task Migracion_y_consumo_atomico_con_conexiones_independientes()
    {
        await EnBaseTemporal(async dbcs =>
        {
            ConstiaDbContext Context()=>new(new DbContextOptionsBuilder<ConstiaDbContext>().UseSqlServer(dbcs).Options);
            var ensayo=Guid.NewGuid();var op=Guid.NewGuid();var who=IdentidadDestinatarioEnsayo.Calcular("c109@example.invalid", new byte[32]);
            await using(var db=Context()) { await db.Database.MigrateAsync();db.Set<AutorizacionEnvioUnico>().Add(new(ensayo,who,op,DateTimeOffset.UtcNow));await db.SaveChangesAsync(); }
            await using(var db=Context()) { db.Set<AutorizacionEnvioUnico>().Add(new(ensayo,who,Guid.NewGuid(),DateTimeOffset.UtcNow));await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync()); }
            await using(var db=Context()) { var repo=new AutorizacionEnvioUnicoRepository(db,TimeProvider.System);Assert.False(await repo.ConsumirAsync(ensayo,new byte[32],op,default));Assert.False(await repo.ConsumirAsync(ensayo,who,Guid.NewGuid(),default)); }
            async Task<bool> Consume(){await using var db=Context();return await new AutorizacionEnvioUnicoRepository(db,TimeProvider.System).ConsumirAsync(ensayo,who,op,default);}
            var concurrent=await Task.WhenAll(Consume(),Consume());Assert.Single(concurrent, x=>x);
            await using(var db=Context()) {var row=await db.Set<AutorizacionEnvioUnico>().AsNoTracking().SingleAsync();Assert.Equal(EstadoAutorizacionEnvio.Consumida,row.Estado);Assert.NotNull(row.ConsumidaEnUtc);var repo=new AutorizacionEnvioUnicoRepository(db,TimeProvider.System);Assert.False(await repo.ConsumirAsync(ensayo,who,op,default));Assert.True(await repo.RegistrarResultadoAsync(ensayo,op,EstadoAutorizacionEnvio.Incierta,default));Assert.False(await repo.RegistrarResultadoAsync(ensayo,op,EstadoAutorizacionEnvio.Aceptada,default));}
            await using(var db=Context()) {var row=await db.Set<AutorizacionEnvioUnico>().AsNoTracking().SingleAsync();Assert.Equal(EstadoAutorizacionEnvio.Incierta,row.Estado);}
        });
    }

    [C109SqlFact]
    public Task Down_con_consumida_rechaza_y_preserva_historial() => VerificarDownConHistorial(consumir: true);

    [C109SqlFact]
    public Task Down_con_preparada_rechaza_y_preserva_historial() => VerificarDownConHistorial(consumir: false);

    private static Task VerificarDownConHistorial(bool consumir) => EnBaseTemporal(async dbcs =>
    {
        var ensayo = Guid.NewGuid();
        var operacion = Guid.NewGuid();
        var identidad = IdentidadDestinatarioEnsayo.Calcular("c120@example.invalid", new byte[32]);
        await using (var db = Contexto(dbcs))
        {
            await db.Database.MigrateAsync();
            db.Set<AutorizacionEnvioUnico>().Add(new(ensayo, identidad, operacion, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        if (consumir)
        {
            await using var db = Contexto(dbcs);
            Assert.True(await new AutorizacionEnvioUnicoRepository(db, TimeProvider.System)
                .ConsumirAsync(ensayo, identidad, operacion, default));
        }
        const string filas = "SELECT * FROM [AutorizacionEnvioUnico] ORDER BY [EnsayoId] FOR JSON PATH, INCLUDE_NULL_VALUES";
        var registros = await Consultar(dbcs, filas);
        var esquema = await Esquema(dbcs, incluirC109: true);
        var historial = await Historial(dbcs, incluirC109: true);
        Assert.Contains(MigracionC109, historial);
        await using (var db = Contexto(dbcs))
        {
            var error = await Assert.ThrowsAsync<SqlException>(() =>
                db.GetService<IMigrator>().MigrateAsync(MigracionAnterior));
            Assert.Equal(51000, error.Number);
            Assert.Equal((byte)1, error.State);
            Assert.Equal("Cannot discard authorization history.", error.Message);
        }
        // Unpooled connections verify committed state after the rejected rollback.
        Assert.Equal(esquema, await Esquema(dbcs, incluirC109: true));
        Assert.Equal(historial, await Historial(dbcs, incluirC109: true));
        Assert.Equal(registros, await Consultar(dbcs, filas));
        await using (var db = Contexto(dbcs))
        {
            var row = await db.Set<AutorizacionEnvioUnico>().AsNoTracking().SingleAsync();
            Assert.Equal(ensayo, row.EnsayoId);
            Assert.Equal(operacion, row.OperacionId);
            Assert.Equal(consumir ? EstadoAutorizacionEnvio.Consumida : EstadoAutorizacionEnvio.Preparada, row.Estado);
            if (consumir)
            {
                Assert.NotNull(row.ConsumidaEnUtc);
                Assert.False(await new AutorizacionEnvioUnicoRepository(db, TimeProvider.System)
                    .ConsumirAsync(ensayo, identidad, operacion, default));
            }
            else Assert.Null(row.ConsumidaEnUtc);
        }
    });

    [C109SqlFact]
    public Task Down_con_tabla_vacia_elimina_solo_C109() => EnBaseTemporal(async dbcs =>
    {
        await using (var db = Contexto(dbcs)) await db.Database.MigrateAsync();
        var esquemaHistorico = await Esquema(dbcs, incluirC109: false);
        var historialAnterior = await Historial(dbcs, incluirC109: false);
        Assert.Contains(MigracionC109, await Historial(dbcs, incluirC109: true));
        Assert.Contains("AutorizacionEnvioUnico", await Esquema(dbcs, incluirC109: true));
        await using (var db = Contexto(dbcs))
        {
            Assert.Empty(await db.Set<AutorizacionEnvioUnico>().ToListAsync());
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior);
        }
        Assert.DoesNotContain("AutorizacionEnvioUnico", await Esquema(dbcs, incluirC109: true));
        Assert.Equal(esquemaHistorico, await Esquema(dbcs, incluirC109: true));
        Assert.Equal(historialAnterior, await Historial(dbcs, incluirC109: true));
        Assert.Contains(MigracionAnterior, await Historial(dbcs, incluirC109: true));
    });

    [Fact]
    public void Down_script_conserva_guard_y_drop_en_misma_transaccion()
    {
        using var db = Contexto("Server=127.0.0.1,1;Database=ScriptOnly;User ID=none;Password=none;TrustServerCertificate=True");
        var script = db.GetService<IMigrator>().GenerateScript(MigracionC109, MigracionAnterior);
        var begin = script.IndexOf("BEGIN TRANSACTION;", StringComparison.Ordinal);
        var guard = script.IndexOf("IF @@TRANCOUNT", StringComparison.Ordinal);
        var drop = script.IndexOf("DROP TABLE [AutorizacionEnvioUnico]", StringComparison.Ordinal);
        var history = script.IndexOf("DELETE FROM [__EFMigrationsHistory]", StringComparison.Ordinal);
        var commit = script.IndexOf("COMMIT;", StringComparison.Ordinal);
        Assert.True(begin >= 0 && begin < guard && guard < drop && drop < history && history < commit, script);
        Assert.Contains("WITH (TABLOCKX, HOLDLOCK)", script);
        Assert.DoesNotContain("COMMIT", script[guard..drop]);
    }

    [C109SqlFact]
    public Task Down_sin_transaccion_rechaza_con_51001() => EnBaseTemporal(async cs =>
    {
        await using var db = Contexto(cs);
        await db.Database.MigrateAsync();
        var esquema = await Esquema(cs, true);
        var historial = await Historial(cs, true);
        var migration = db.GetService<IMigrationsAssembly>().CreateMigration(
            db.GetService<IMigrationsAssembly>().Migrations[MigracionC109], db.Database.ProviderName!);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.False(guard.SuppressTransaction);
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(cs) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using (var setup = new SqlCommand("SET IMPLICIT_TRANSACTIONS OFF; SELECT @@TRANCOUNT;", connection))
            Assert.Equal(0, Convert.ToInt32(await setup.ExecuteScalarAsync()));
        await using var command = new SqlCommand(guard.Sql, connection);
        var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        AssertError(error, 51001, "Authorization rollback requires an active committable transaction.");
        Assert.Equal(esquema, await Esquema(cs, true));
        Assert.Equal(historial, await Historial(cs, true));
    });

    [C109SqlFact]
    public Task Down_insercion_primero_espera_y_preserva_confirmada() => EnBaseTemporal(async cs =>
    {
        await using (var setup = Contexto(cs)) await setup.Database.MigrateAsync();
        var esquema = await Esquema(cs, true);
        var historial = await Historial(cs, true);
        var ensayo = Guid.NewGuid();
        var operacion = Guid.NewGuid();
        await using var b = new SqlConnection(cs);
        await b.OpenAsync();
        await using var tx = (SqlTransaction)await b.BeginTransactionAsync();
        await Insertar(b, tx, ensayo, operacion);
        var pause = new PausaDown(antesDeDrop: false);
        await using var a = ContextoConcurrente(cs, pause);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var down = a.GetService<IMigrator>().MigrateAsync(MigracionAnterior, deadline.Token);
        try
        {
            var command = await pause.Alcanzada.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var sessionA = ((SqlConnection)command.Connection!).ServerProcessId;
            var wait = await EsperarBloqueo(b, tx, sessionA, b.ServerProcessId, deadline.Token);
            output.WriteLine($"Insercion primero: A={sessionA} bloqueada por B={b.ServerProcessId}; espera={wait}; B confirma despues de observar el bloqueo.");
            await tx.CommitAsync();
            var error = await Assert.ThrowsAsync<SqlException>(() => down);
            AssertError(error, 51000, "Cannot discard authorization history.");
        }
        finally { pause.Continuar.TrySetResult(); }
        Assert.Equal(esquema, await Esquema(cs, true));
        Assert.Equal(historial, await Historial(cs, true));
        await using var verification = Contexto(cs);
        var row = await verification.Set<AutorizacionEnvioUnico>().AsNoTracking().SingleAsync();
        Assert.Equal(ensayo, row.EnsayoId);
        Assert.Equal(operacion, row.OperacionId);
        Assert.Equal(EstadoAutorizacionEnvio.Preparada, row.Estado);
        var repo = new AutorizacionEnvioUnicoRepository(verification, TimeProvider.System);
        Assert.True(await repo.ConsumirAsync(ensayo, row.IdentidadDestinatario, operacion, default));
        Assert.False(await repo.ConsumirAsync(ensayo, row.IdentidadDestinatario, operacion, default));
    });

    [C109SqlFact]
    public Task Down_primero_bloquea_insercion_hasta_1222_y_elimina_vacia() => EnBaseTemporal(async cs =>
    {
        await using (var setup = Contexto(cs)) await setup.Database.MigrateAsync();
        var esquema = await Esquema(cs, false);
        var historial = await Historial(cs, false);
        var pause = new PausaDown(antesDeDrop: true);
        await using var a = ContextoConcurrente(cs, pause);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var down = a.GetService<IMigrator>().MigrateAsync(MigracionAnterior, deadline.Token);
        try
        {
            var command = await pause.Alcanzada.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var connection = (SqlConnection)command.Connection!;
            var transaction = (SqlTransaction)command.Transaction!;
            await using (var check = new SqlCommand("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE request_session_id=@@SPID AND resource_type='OBJECT' AND resource_associated_entity_id=OBJECT_ID('AutorizacionEnvioUnico') AND request_mode='X' AND request_status='GRANT';", connection, transaction))
                Assert.Equal(1, Convert.ToInt32(await check.ExecuteScalarAsync()));
            await using var b = new SqlConnection(cs);
            await b.OpenAsync();
            await using (var timeout = new SqlCommand("SET LOCK_TIMEOUT 10000;", b)) await timeout.ExecuteNonQueryAsync();
            var insert = Insertar(b, null, Guid.NewGuid(), Guid.NewGuid());
            var wait = await EsperarBloqueo(connection, transaction, b.ServerProcessId, connection.ServerProcessId, deadline.Token);
            output.WriteLine($"Down primero: X de tabla concedido a A={connection.ServerProcessId}; B={b.ServerProcessId} bloqueada por A; espera={wait}.");
            var error = await Assert.ThrowsAsync<SqlException>(() => insert);
            Assert.Equal(1222, error.Number);
            output.WriteLine("B rechazada con 1222 antes de liberar A; ninguna insercion confirmada.");
            await using (var count = new SqlCommand("SELECT COUNT(*) FROM [AutorizacionEnvioUnico];", connection, transaction))
                Assert.Equal(0, Convert.ToInt32(await count.ExecuteScalarAsync()));
        }
        finally { pause.Continuar.TrySetResult(); }
        await down;
        Assert.DoesNotContain("AutorizacionEnvioUnico", await Esquema(cs, true));
        Assert.Equal(esquema, await Esquema(cs, true));
        Assert.Equal(historial, await Historial(cs, true));
    });

    private static void AssertError(SqlException error, int number, string message)
    {
        Assert.Equal(number, error.Number);
        Assert.Equal((byte)1, error.State);
        Assert.Equal(message, error.Message);
    }

    private static async Task Insertar(SqlConnection connection, SqlTransaction? transaction, Guid ensayo, Guid operacion)
    {
        await using var command = new SqlCommand("INSERT INTO [AutorizacionEnvioUnico] ([EnsayoId],[OperacionId],[Estado],[CreadaEnUtc],[IdentidadDestinatario]) VALUES (@ensayo,@operacion,0,SYSUTCDATETIME(),CONVERT(binary(32),1));", connection, transaction);
        command.Parameters.AddWithValue("@ensayo", ensayo);
        command.Parameters.AddWithValue("@operacion", operacion);
        command.CommandTimeout = 20;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> EsperarBloqueo(SqlConnection observer, SqlTransaction? transaction, int waiting, int blocker, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT wait_type FROM sys.dm_exec_requests WHERE session_id=@waiting AND blocking_session_id=@blocker AND wait_type LIKE 'LCK%';", observer, transaction);
        command.Parameters.AddWithValue("@waiting", waiting);
        command.Parameters.AddWithValue("@blocker", blocker);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (await command.ExecuteScalarAsync(ct) is string wait) return wait;
            // Poll an actual SQL wait condition; the delay does not establish ordering.
            await Task.Delay(25, ct);
        }
    }

    private static ConstiaDbContext ContextoConcurrente(string cs, PausaDown pause) => new(
        new DbContextOptionsBuilder<ConstiaDbContext>().UseSqlServer(cs, sql => sql.CommandTimeout(30)).AddInterceptors(pause).Options);

    private sealed class PausaDown(bool antesDeDrop) : DbCommandInterceptor
    {
        public TaskCompletionSource<DbCommand> Alcanzada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continuar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var target = antesDeDrop ? command.CommandText.Contains("DROP TABLE [AutorizacionEnvioUnico]") : command.CommandText.Contains("Cannot discard authorization history.");
            if (target && Alcanzada.TrySetResult(command))
            {
                Assert.NotNull(command.Transaction);
                if (antesDeDrop) await Continuar.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    private const string MigracionC109 = "20261030232834_AddAutorizacionEnvioUnico";
    private const string MigracionAnterior = "20261023003711_AddWelcomeEmailOutbox";

    private static ConstiaDbContext Contexto(string connection) => new(
        new DbContextOptionsBuilder<ConstiaDbContext>().UseSqlServer(connection).Options);

    private static Task<string> Historial(string connection, bool incluirC109) => Consultar(connection,
        "SELECT [MigrationId], [ProductVersion] FROM [__EFMigrationsHistory] " +
        (incluirC109 ? "" : $"WHERE [MigrationId] <> '{MigracionC109}' ") +
        "ORDER BY [MigrationId] FOR JSON PATH");

    private static Task<string> Esquema(string connection, bool incluirC109) => Consultar(connection,
        """
        SELECT t.name AS Tabla,
            (SELECT c.name, TYPE_NAME(c.user_type_id) AS Tipo, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, dc.definition AS Defecto
             FROM sys.columns c LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
             WHERE c.object_id=t.object_id ORDER BY c.column_id FOR JSON PATH, INCLUDE_NULL_VALUES) AS Columnas,
            (SELECT i.name, i.is_unique, i.is_primary_key, i.type_desc, i.filter_definition,
                (SELECT COL_NAME(ic.object_id,ic.column_id) AS Columna, ic.key_ordinal, ic.is_descending_key, ic.is_included_column
                 FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                 ORDER BY ic.index_column_id FOR JSON PATH) AS Columnas
             FROM sys.indexes i WHERE i.object_id=t.object_id ORDER BY i.name FOR JSON PATH, INCLUDE_NULL_VALUES) AS Indices,
            (SELECT ck.name, ck.definition, ck.is_disabled, ck.is_not_trusted FROM sys.check_constraints ck
             WHERE ck.parent_object_id=t.object_id ORDER BY ck.name FOR JSON PATH) AS Restricciones,
            (SELECT fk.name, OBJECT_NAME(fk.referenced_object_id) AS Destino, fk.delete_referential_action, fk.update_referential_action
             FROM sys.foreign_keys fk WHERE fk.parent_object_id=t.object_id ORDER BY fk.name FOR JSON PATH) AS Relaciones
        FROM sys.tables t
        """ + (incluirC109 ? "" : " WHERE t.name <> 'AutorizacionEnvioUnico' ") +
        " ORDER BY t.name FOR JSON PATH");

    private static async Task<string> Consultar(string connection, string sql)
    {
        var builder = new SqlConnectionStringBuilder(connection) { Pooling = false };
        await using var c = new SqlConnection(builder.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(sql, c);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new System.Text.StringBuilder();
        while (await reader.ReadAsync()) result.Append(reader.GetString(0));
        return result.ToString();
    }

    private static async Task EnBaseTemporal(Func<string, Task> prueba)
    {
        var container = Environment.GetEnvironmentVariable("CONSTIA_C109_CONTAINER_ID") ?? "";
        var connection = Environment.GetEnvironmentVariable("CONSTIA_C109_SQL_CONNECTION") ?? "";
        if (!Regex.IsMatch(container, "^[0-9a-f]{64}$")) throw new InvalidOperationException("Invalid disposable C109 container identity.");
        var docker = new ProcessStartInfo("docker") { RedirectStandardOutput=true, RedirectStandardError=true, UseShellExecute=false };
        foreach (var part in new[] { "--context", "desktop-linux", "inspect", "--format", "{{.Id}} {{index .Config.Labels \"constia.c109.owner\"}}", container }) docker.ArgumentList.Add(part);
        using var process=Process.Start(docker) ?? throw new InvalidOperationException("Cannot inspect C109 container.");
        var inspected=await process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync();
        if (process.ExitCode!=0 || inspected.Trim()!=container+" disposable-sql") throw new InvalidOperationException("Unverified C109 container.");
        var admin=new SqlConnectionStringBuilder(connection);
        if (!Regex.IsMatch(admin.DataSource, "^127[.]0[.]0[.]1,[0-9]{1,5}$") || admin.InitialCatalog!="master" || admin.UserID!="sa" || string.IsNullOrEmpty(admin.Password)) throw new InvalidOperationException("C109 endpoint is not isolated loopback.");
        await using(var c=new SqlConnection(admin.ConnectionString))
        {
            await c.OpenAsync(); await using var cmd=new SqlCommand("SELECT CONVERT(int,SERVERPROPERTY('ProductMajorVersion')), CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))",c);
            await using var reader=await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync() || reader.GetInt32(0)!=16 || reader.GetString(1)!=container[..12]) throw new InvalidOperationException("SQL container identity differs.");
        }
        var name="Constia_C109_"+Guid.NewGuid().ToString("N");
        var dbcs=new SqlConnectionStringBuilder(admin.ConnectionString){InitialCatalog=name}.ConnectionString;
        async Task Admin(string sql) { await using var c=new SqlConnection(admin.ConnectionString);await c.OpenAsync();await using var cmd=new SqlCommand(sql,c);await cmd.ExecuteNonQueryAsync(); }
        await Admin($"CREATE DATABASE [{name}]");
        try
        {
            await prueba(dbcs);
        }
        finally
        {
            if(!Regex.IsMatch(name,"^Constia_C109_[0-9a-f]{32}$"))throw new InvalidOperationException("Refusing cleanup of unowned database.");
            using(var pool=new SqlConnection(dbcs)) SqlConnection.ClearPool(pool);
            await Admin($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");
        }
    }
}
