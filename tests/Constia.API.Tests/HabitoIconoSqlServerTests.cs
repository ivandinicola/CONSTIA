using System.Text.Json;
using System.Text.RegularExpressions;
using Constia.Domain;
using Constia.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Constia.API.Tests;

// Opt-in CI: valida SQL Server 2022 y la identidad del servicio descartable antes de crear bases.
public sealed class HabitoIconoSqlServerTests
{
    private const string PreviousMigration = "20261019204255_AddHabitCategories";
    private const string CurrentMigration = "20261020071619_AddHabitIconId";
    private const string EnabledVariable = "CONSTIA_HABCAT01_SQLSERVER_TESTS";
    private const string ConnectionVariable = "CONSTIA_HABCAT01_SQLSERVER_CONNECTION";
    private const string ContainerVariable = "CONSTIA_HABCAT01_CONTAINER_ID";

    [HabCatSqlFact]
    public async Task Up_PreservaHistoriaCompletaCategoriaYFkYAgregaSoloColumnaNullable()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await SeedHistoryAsync(context);
        var before = await ReadHistoricalDataAsync(database);

        await context.GetService<IMigrator>().MigrateAsync(CurrentMigration);

        Assert.Equal(before, await ReadHistoricalDataAsync(database));
        Assert.Equal("nvarchar:128:1:0", await context.Database.SqlQueryRaw<string>("""
            SELECT CONCAT(TYPE_NAME([system_type_id]),':',[max_length],':',[is_nullable],':',[default_object_id]) AS [Value]
            FROM sys.columns WHERE [object_id]=OBJECT_ID(N'Habito') AND [name]=N'IconoId'
            """).SingleAsync());
        var habits = await context.Habitos.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, habits.Length);
        Assert.All(habits, h => Assert.Null(h.IconoId));
        Assert.Single(habits, h => h.CategoriaId is null);
        Assert.Single(habits, h => h.CategoriaId == CatalogoCategoriasHabito.Todas[4].Id);
        await VerifyCategoryFkAsync(context);
    }

    [HabCatSqlFact]
    public async Task Asignaciones_SeMaterializanYSePropaganPorMvcSinCambiarFechasNiHistoria()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(CurrentMigration);
        await SeedHistoryAsync(context);
        var before = await ReadHistoricalDataAsync(database);
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE [Habito] SET [IconoId]=N'libro' WHERE [CategoriaId] IS NOT NULL");
        context.ChangeTracker.Clear();
        var habits = await context.Habitos.AsNoTracking()
            .Include(h => h.Usuario)
            .Include(h => h.Programaciones).ThenInclude(p => p.DiasProgramados)
            .Include(h => h.PeriodosEstado).ToArrayAsync();
        var assigned = Assert.Single(habits, h => h.CategoriaId is not null);
        Assert.Equal("libro", assigned.IconoId);
        Assert.Equal(CatalogoCategoriasHabito.Todas[4].Id, assigned.CategoriaId);
        var historical = Assert.Single(habits, h => h.CategoriaId is null);
        Assert.Null(historical.IconoId);

        using var factory = new JwtApiFactory();
        factory.UsuariosPersistidos[assigned.Usuario.Id] = assigned.Usuario;
        factory.ReemplazarHabitos(habits);
        using var client = HabitoMetadataHttpTests.ClienteAutenticado(factory, assigned.Usuario);
        foreach (var route in new[] { "/api/habitos", $"/api/habitos/{assigned.Id:D}" })
        {
            var response = await client.GetAsync(route);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var body = json.RootElement.ValueKind == JsonValueKind.Array
                ? json.RootElement.EnumerateArray().Single() : json.RootElement;
            HabitoMetadataHttpTests.VerificarContrato(body, assigned, assigned.CategoriaId, "libro");
        }
        Assert.Equal(before, await ReadHistoricalDataAsync(database));
        Assert.Equal(0, factory.CantidadDeHabitosGuardados);
        await VerifyCategoryFkAsync(context);
    }

    [HabCatSqlFact]
    public async Task Down_ConIconoRechazaSinPerdidaParcialDeDatosEsquemaNiAsignaciones()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        await using (var setup = database.CreateContext())
        {
            await setup.GetService<IMigrator>().MigrateAsync(CurrentMigration);
            await SeedHistoryAsync(setup);
            await setup.Database.ExecuteSqlRawAsync(
                "UPDATE [Habito] SET [IconoId]=N'libro' WHERE [CategoriaId] IS NOT NULL");
        }
        var before = await ReadHistoricalDataAsync(database, includeIcon: true);
        await using (var rollback = database.CreateContext())
        {
            var error = await Assert.ThrowsAsync<SqlException>(
                () => rollback.GetService<IMigrator>().MigrateAsync(PreviousMigration));
            Assert.Equal(51021, error.Number);
            Assert.Contains("HAB-CAT-01", error.Message);
        }
        await using var verify = database.CreateContext();
        Assert.Equal(before, await ReadHistoricalDataAsync(database, includeIcon: true));
        Assert.Equal(CurrentMigration, (await verify.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal("libro", (await verify.Habitos.AsNoTracking().SingleAsync(h => h.CategoriaId != null)).IconoId);
        Assert.Equal(10, await verify.CategoriasHabito.CountAsync());
        await VerifyCategoryFkAsync(verify);
    }

    [HabCatSqlFact]
    public async Task Down_SoloNullPermiteRollbackYUpNuevamenteConHistoriaIntacta()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(CurrentMigration);
        await SeedHistoryAsync(context);
        var before = await ReadHistoricalDataAsync(database);

        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        Assert.Equal(before, await ReadHistoricalDataAsync(database));
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.columns
            WHERE [object_id]=OBJECT_ID(N'Habito') AND [name]=N'IconoId'
            """).SingleAsync());
        Assert.Equal(PreviousMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        await VerifyCategoryFkAsync(context);

        await context.GetService<IMigrator>().MigrateAsync(CurrentMigration);

        Assert.Equal(before, await ReadHistoricalDataAsync(database));
        Assert.Equal(CurrentMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        Assert.All(await context.Habitos.AsNoTracking().ToArrayAsync(), h => Assert.Null(h.IconoId));
        await VerifyCategoryFkAsync(context);
    }

    private static async Task VerifyCategoryFkAsync(ConstiaDbContext context)
    {
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.foreign_keys
            WHERE [name]=N'FK_Habito_CategoriaHabito_CategoriaId'
              AND [is_disabled]=0 AND [is_not_trusted]=0 AND [delete_referential_action]=0
            """).SingleAsync());
    }

    private static async Task SeedHistoryAsync(ConstiaDbContext context)
    {
        var userId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        var completionId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Usuario] ([Id],[Nombre],[Email],[PasswordHash],[TimeZoneId])
            VALUES ({userId},N'HAB-CAT-01',N'habcat01@example.invalid',N'test-hash',N'Etc/UTC');
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
        var categoryId = CatalogoCategoriasHabito.Todas[4].Id;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Habito] SET [CategoriaId]={categoryId} WHERE [Id]={activeId}");
    }

    private static async Task<string[]> ReadHistoricalDataAsync(DisposableDatabase database, bool includeIcon = false)
    {
        // Campos previos de Habito: la columna nueva es la única diferencia permitida.
        string[] queries =
        [
            $"SELECT [Id],[Nombre],[Descripcion],[CreatedAt],[StartDate],[Estado],[UsuarioId],[ConcurrencyVersion],[CategoriaId]{(includeIcon ? ",[IconoId]" : "")} FROM [Habito] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES",
            "SELECT * FROM [CategoriaHabito] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES",
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
        private readonly string _name;
        public string ConnectionString { get; }

        private DisposableDatabase(string admin, string name)
        {
            _adminConnection = admin;
            _name = name;
            ConnectionString = new SqlConnectionStringBuilder(admin) { InitialCatalog = name }.ConnectionString;
        }

        internal ConstiaDbContext CreateContext() => new(new DbContextOptionsBuilder<ConstiaDbContext>()
            .UseSqlServer(ConnectionString).Options);

        internal static async Task<DisposableDatabase> CreateAsync()
        {
            var admin = await VerifyAdminAsync();
            var database = new DisposableDatabase(admin, $"Constia_HabCat01_{Guid.NewGuid():N}");
            await using var connection = new SqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{database._name}];", connection);
            await command.ExecuteNonQueryAsync();
            return database;
        }

        public async ValueTask DisposeAsync()
        {
            if (!Regex.IsMatch(_name, "^Constia_HabCat01_[0-9a-f]{32}$")
                || new SqlConnectionStringBuilder(ConnectionString).InitialCatalog != _name)
                throw new InvalidOperationException("Refusing cleanup of a non-HAB-CAT-01 database.");
            var admin = await VerifyAdminAsync();
            if (admin != _adminConnection)
                throw new InvalidOperationException("The disposable SQL destination changed.");
            using (var ownPool = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(ownPool);
            await using var connection = new SqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                $"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}];", connection);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<string> VerifyAdminAsync()
        {
            if (!IsEnabled()
                || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
                throw new InvalidOperationException("HAB-CAT-01 requires its explicitly enabled disposable GitHub Actions service.");
            var container = Environment.GetEnvironmentVariable(ContainerVariable) ?? "";
            if (!Regex.IsMatch(container, "^[0-9a-f]{64}$"))
                throw new InvalidOperationException("Missing disposable SQL service container identity.");
            var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ConnectionVariable));
            if (builder.DataSource != "127.0.0.1,1433" || builder.InitialCatalog != "master"
                || builder.UserID != "sa" || string.IsNullOrWhiteSpace(builder.Password))
                throw new InvalidOperationException("Only the disposable runner SQL endpoint is allowed.");
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT CONVERT(int,SERVERPROPERTY('ProductMajorVersion')),CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'));", connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            if (reader.GetInt32(0) != 16 || reader.GetString(1) != container[..12])
                throw new InvalidOperationException("SQL Server 2022 service identity does not match the disposable CI container.");
            return builder.ConnectionString;
        }
    }

    private static bool IsEnabled() =>
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
        && Environment.GetEnvironmentVariable(EnabledVariable) == "true"
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable));

    private sealed class HabCatSqlFactAttribute : FactAttribute
    {
        public HabCatSqlFactAttribute()
        {
            if (!IsEnabled()) Skip = "Requires the explicitly enabled disposable HAB-CAT-01 SQL Server CI service.";
        }
    }
}
