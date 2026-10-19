using System.Text.RegularExpressions;
using Constia.Domain;
using Constia.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Constia.API.Tests;

// Opt-in independiente: nunca toma la conexión habitual de la aplicación.
public sealed class CategoriaHabitoSqlServerTests
{
    private const string PreviousMigration = "20261016194046_ConstrainPeriodoEstadoHabitoState";

    [DisposableSqlFact]
    public async Task BaseVacia_PrecargaExactaYMaterializacionEfConConstructorExistente()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var categories = await context.CategoriasHabito.AsNoTracking().OrderBy(c => c.Orden).ToArrayAsync();
        Assert.Equal(10, categories.Length);
        Assert.Equal(CatalogoCategoriasHabito.Todas.Select(c => (c.Id, c.Nombre, c.Orden)),
            categories.Select(c => (c.Id, c.Nombre, c.Orden)));
    }

    [DisposableSqlFact]
    public async Task BaseHistorica_PreservaHabitosFechasProgramacionesPeriodosCumplimientosYSnapshot()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        // Dos estados históricos, fechas y versiones no triviales; sin ejecutar backfill.
        var userId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        var completionId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Usuario] ([Id],[Nombre],[Email],[PasswordHash],[TimeZoneId])
            VALUES ({userId},N'CAT-02',N'cat02@example.invalid',N'test-hash',N'Etc/UTC');
            INSERT INTO [Habito] ([Id],[Nombre],[Descripcion],[CreatedAt],[StartDate],[Estado],[UsuarioId],[ConcurrencyVersion])
            VALUES ({activeId},N'Activo',N'Historia', '2024-01-01T02:30:00+00:00','2024-01-01',0,{userId},7),
                   ({inactiveId},N'Inactivo',NULL,'2024-01-02T03:30:00+00:00','2024-01-02',1,{userId},11);
            INSERT INTO [ProgramacionHabito] ([HabitoId],[VigenteDesde],[VigenteHasta])
            VALUES ({activeId},'2024-01-01','2025-01-01'),({activeId},'2025-01-01',NULL),({inactiveId},'2024-01-02',NULL);
            INSERT INTO [ProgramacionHabitoDia] ([HabitoId],[VigenteDesde],[Dia])
            VALUES ({activeId},'2024-01-01',1),({activeId},'2025-01-01',2),({inactiveId},'2024-01-02',3);
            INSERT INTO [PeriodoEstadoHabito] ([HabitoId],[VigenteDesde],[VigenteHasta],[Estado])
            VALUES ({activeId},'2024-01-01',NULL,0),({inactiveId},'2024-01-02','2025-01-01',0),({inactiveId},'2025-01-01',NULL,1);
            INSERT INTO [Cumplimiento] ([Id],[HabitoId],[Fecha]) VALUES ({completionId},{activeId},'2024-01-01');
            INSERT INTO [SnapshotEstadoHabito] ([HabitoId],[TimeZoneId],[FechaCorteLocal],[FechaInicio],[EstadoAlCorte])
            VALUES ({activeId},N'Etc/UTC','2026-10-16','2024-01-01',0),({inactiveId},N'Etc/UTC','2026-10-16','2024-01-02',1);
            INSERT INTO [InicializacionEstadoHabito] ([Id],[CutoverUtc],[SnapshotCapturedAtUtc],[ExpectedLegacyHabitCount],[CompletedAtUtc])
            VALUES (1,'2026-10-16T02:30:00+00:00','2026-10-16T03:00:00+00:00',2,'2026-10-16T04:00:00+00:00');
            """);
        var before = await ReadHistoricalDataAsync(database);
        await context.Database.MigrateAsync();
        var after = await ReadHistoricalDataAsync(database);
        Assert.Equal(before, after);
        var habits = await context.Habitos.AsNoTracking().OrderBy(h => h.Nombre).ToArrayAsync();
        Assert.Equal(2, habits.Length);
        Assert.Equal(new[] { activeId, inactiveId }.Order(), habits.Select(h => h.Id).Order());
        Assert.All(habits, h => Assert.Null(h.CategoriaId));
    }

    [DisposableSqlFact]
    public async Task Fk_PermiteOficialONullRechazaDesconocidaYNoEliminaHabitoEnCascada()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var user = new Usuario("CAT-02", "cat02@example.invalid", "test-hash", "Etc/UTC");
        var categorized = new Habito(user, "Con categoria", null, new DateOnly(2026, 10, 1), [DayOfWeek.Monday]);
        var historical = new Habito(user, "Sin categoria", null, new DateOnly(2026, 10, 1), [DayOfWeek.Monday]);
        context.AddRange(categorized, historical);
        var officialId = CatalogoCategoriasHabito.Todas[4].Id;
        context.Entry(categorized).Property(h => h.CategoriaId).CurrentValue = officialId;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(officialId, (await context.Habitos.SingleAsync(h => h.Id == categorized.Id)).CategoriaId);
        Assert.Null((await context.Habitos.SingleAsync(h => h.Id == historical.Id)).CategoriaId);
        var unknown = Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000099");
        var invalid = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Habito] SET [CategoriaId]={unknown} WHERE [Id]={historical.Id}"));
        Assert.Equal(547, invalid.Number);
        var delete = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [CategoriaHabito] WHERE [Id]={officialId}"));
        Assert.Equal(547, delete.Number);
        Assert.Equal(2, await context.Habitos.CountAsync());
        Assert.Equal(10, await context.CategoriasHabito.CountAsync());
        context.ChangeTracker.Clear();
        Assert.Equal(officialId, (await context.Habitos.SingleAsync(h => h.Id == categorized.Id)).CategoriaId);
    }

    [DisposableSqlFact]
    public async Task Down_SinAsignacionesRevierteSoloCategoriaYPreservaDatosHistoricos()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync();
            var user = new Usuario("Rollback", "rollback@example.invalid", "test-hash", "Etc/UTC");
            context.Add(new Habito(user, "Sin categoria", null, new DateOnly(2026, 10, 1), [DayOfWeek.Monday]));
            await context.SaveChangesAsync();
        }
        var before = await ReadHistoricalDataAsync(database);
        await using (var rollback = database.CreateContext())
        {
            await rollback.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }
        await using var verify = database.CreateContext();
        Assert.Equal(before, await ReadHistoricalDataAsync(database));
        Assert.Equal(0, await verify.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'CategoriaHabito'
            """).SingleAsync());
        Assert.Equal(0, await verify.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.columns
            WHERE [object_id] = OBJECT_ID(N'Habito') AND [name] = N'CategoriaId'
            """).SingleAsync());
        Assert.Equal(0, await verify.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE [name] = N'FK_Habito_CategoriaHabito_CategoriaId'
            """).SingleAsync());
        Assert.Equal(0, await verify.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'Habito') AND [name] = N'IX_Habito_CategoriaId'
            """).SingleAsync());
        Assert.Equal(PreviousMigration, (await verify.Database.GetAppliedMigrationsAsync()).Last());
    }

    [DisposableSqlFact]
    public async Task Down_ConAsignacionRechazaSinPerdidaParcialNiCambiosDeEsquema()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var categoryId = CatalogoCategoriasHabito.Todas[4].Id;
        Guid habitId;
        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync();
            var user = new Usuario("Rollback", "rollback@example.invalid", "test-hash", "Etc/UTC");
            var habit = new Habito(user, "Con categoria", null, new DateOnly(2026, 10, 1), [DayOfWeek.Monday]);
            context.Add(habit);
            context.Entry(habit).Property(h => h.CategoriaId).CurrentValue = categoryId;
            await context.SaveChangesAsync();
            habitId = habit.Id;
        }
        var before = await ReadHistoricalDataAsync(database);
        await using (var rollback = database.CreateContext())
        {
            var error = await Assert.ThrowsAsync<SqlException>(() =>
                rollback.GetService<IMigrator>().MigrateAsync(PreviousMigration));
            Assert.Equal(51020, error.Number);
            Assert.Contains("CAT-02", error.Message);
        }
        // Otro contexto/conexión comprueba el estado persistido después del rechazo.
        await using var verify = database.CreateContext();
        Assert.Equal(before, await ReadHistoricalDataAsync(database));
        var categories = await verify.CategoriasHabito.AsNoTracking().OrderBy(c => c.Orden).ToArrayAsync();
        Assert.Equal(CatalogoCategoriasHabito.Todas.Select(c => (c.Id, c.Nombre, c.Orden)),
            categories.Select(c => (c.Id, c.Nombre, c.Orden)));
        Assert.Equal(categoryId, (await verify.Habitos.AsNoTracking().SingleAsync(h => h.Id == habitId)).CategoriaId);
        Assert.Equal(1, await verify.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.foreign_keys
            WHERE [name] = N'FK_Habito_CategoriaHabito_CategoriaId' AND [is_disabled] = 0 AND [is_not_trusted] = 0
            """).SingleAsync());
        Assert.Equal(1, await verify.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'Habito') AND [name] = N'IX_Habito_CategoriaId'
            """).SingleAsync());
        Assert.EndsWith("_AddHabitCategories", (await verify.Database.GetAppliedMigrationsAsync()).Last());
    }

    private static async Task<string[]> ReadHistoricalDataAsync(DisposableDatabase database)
    {
        // Campos previos de Habito: la columna nueva es la única diferencia permitida.
        string[] queries =
        [
            "SELECT [Id],[Nombre],[Descripcion],[CreatedAt],[StartDate],[Estado],[UsuarioId],[ConcurrencyVersion] FROM [Habito] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES",
            "SELECT * FROM [Usuario] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES",
            "SELECT * FROM [ProgramacionHabito] ORDER BY [HabitoId],[VigenteDesde] FOR JSON PATH, INCLUDE_NULL_VALUES",
            "SELECT * FROM [ProgramacionHabitoDia] ORDER BY [HabitoId],[VigenteDesde],[Dia] FOR JSON PATH, INCLUDE_NULL_VALUES",
            "SELECT * FROM [PeriodoEstadoHabito] ORDER BY [HabitoId],[VigenteDesde] FOR JSON PATH, INCLUDE_NULL_VALUES",
            "SELECT * FROM [Cumplimiento] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES",
            "SELECT * FROM [SnapshotEstadoHabito] ORDER BY [HabitoId] FOR JSON PATH, INCLUDE_NULL_VALUES",
            "SELECT * FROM [InicializacionEstadoHabito] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES"
        ];
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var result = new List<string>();
        foreach (var query in queries)
        {
            await using var command = new SqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();
            var json = new System.Text.StringBuilder();
            while (await reader.ReadAsync()) json.Append(reader.GetString(0));
            result.Add(json.ToString());
        }
        return result.ToArray();
    }

    private sealed class DisposableDatabase : IAsyncDisposable
    {
        private readonly string _adminConnection;
        private readonly Cat02SqlRunnerIdentity _identity;
        private readonly string _password;
        private readonly string _name;
        public string ConnectionString { get; }

        private DisposableDatabase(string adminConnection, string name, Cat02SqlRunnerIdentity identity, string password)
        {
            _adminConnection = adminConnection;
            _identity = identity;
            _password = password;
            _name = name;
            ConnectionString = new SqlConnectionStringBuilder(adminConnection) { InitialCatalog = name }.ConnectionString;
        }

        public ConstiaDbContext CreateContext() => new(new DbContextOptionsBuilder<ConstiaDbContext>()
            .UseSqlServer(ConnectionString).Options);

        public static async Task<DisposableDatabase> CreateAsync()
        {
            var identity = Cat02SqlRunnerIdentity.Read();
            var password = Environment.GetEnvironmentVariable("CONSTIA_CAT02_PASSWORD") ?? "";
            var adminConnection = await identity.VerifyAdminAsync(password);
            var database = new DisposableDatabase(adminConnection, $"Constia_Cat02_{Guid.NewGuid():N}", identity, password);
            await using var connection = new SqlConnection(database._adminConnection);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{database._name}];", connection);
            await command.ExecuteNonQueryAsync();
            return database;
        }

        public async ValueTask DisposeAsync()
        {
            if (!Regex.IsMatch(_name, "^Constia_Cat02_[0-9a-f]{32}$")
                || new SqlConnectionStringBuilder(ConnectionString).InitialCatalog != _name)
                throw new InvalidOperationException("Refusing cleanup of a non-CAT-02 database.");
            await _identity.VerifyAdminAsync(_password);
            using (var ownPool = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(ownPool);
            await using var connection = new SqlConnection(_adminConnection);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                $"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}];", connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class DisposableSqlFactAttribute : FactAttribute
    {
        public DisposableSqlFactAttribute()
        {
            if (!Cat02SqlRunnerIdentity.IsEnabled()) Skip = "Requires the explicitly enabled disposable CAT-02 SQL Server CI runner.";
        }
    }
}
