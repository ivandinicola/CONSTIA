using Constia.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Data;

namespace Constia.API.Tests;

public sealed class Temp004ConcurrencyMigrationSqlServerTests
{
    private const string PreviousMigration = "20261009004634_VersionarProgramacionHabitos";
    private const string TargetMigration = "20261011023329_AddHabitoConcurrencyVersion";
    private const string ConnectionEnvironmentVariable = "CONSTIA_TEMP004_SQLSERVER_CONNECTION";
    private const string IntegrationEnabledEnvironmentVariable = "CONSTIA_TEMP004_SQLSERVER_TESTS";

    [SqlServerIntegrationFact]
    public async Task Up_BackfillsHabitosYConservaHabitosProgramacionesYCumplimientos()
    {
        var adminConnectionString = GetDisposableRunnerConnectionString();
        var database = await CreateDisposableDatabaseAsync(adminConnectionString);
        try
        {
            await MigrateToAsync(database.ConnectionString, PreviousMigration);
            var habits = CreateHabits();
            await SeedExistingDataAsync(database.ConnectionString, habits);

            var habitsBefore = await ReadHabitsAsync(database.ConnectionString);
            var schedulesBefore = await ReadSchedulesAsync(database.ConnectionString);
            var completionsBefore = await ReadCompletionsAsync(database.ConnectionString);

            await MigrateToAsync(database.ConnectionString, TargetMigration);

            var habitsAfter = await ReadVersionedHabitsAsync(database.ConnectionString);
            var schedulesAfter = await ReadSchedulesAsync(database.ConnectionString);
            var completionsAfter = await ReadCompletionsAsync(database.ConnectionString);

            Assert.True(habitsBefore.SetEquals(habitsAfter.Select(item => item.Habit)));
            Assert.Equal(habits.Length, habitsAfter.Count);
            Assert.All(habitsAfter, item => Assert.Equal(1L, item.ConcurrencyVersion));
            Assert.True(schedulesBefore.SetEquals(schedulesAfter));
            Assert.Equal(habits.Length, await ScalarIntAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM [ProgramacionHabito];"));
            Assert.Equal(habits.Length, await ScalarIntAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM [ProgramacionHabito] WHERE [VigenteHasta] IS NULL;"));
            Assert.True(completionsBefore.SetEquals(completionsAfter));
            Assert.True(await MigrationIsAppliedAsync(database.ConnectionString, TargetMigration));
            await AssertConcurrencyColumnAsync(database.ConnectionString);
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Up_ConHabitoVacioCreaLaColumnaSinDefaultPermanente()
    {
        var adminConnectionString = GetDisposableRunnerConnectionString();
        var database = await CreateDisposableDatabaseAsync(adminConnectionString);
        try
        {
            await MigrateToAsync(database.ConnectionString, PreviousMigration);
            Assert.Equal(0, await ScalarIntAsync(database.ConnectionString, "SELECT COUNT(*) FROM [Habito];"));

            await MigrateToAsync(database.ConnectionString, TargetMigration);

            Assert.Equal(0, await ScalarIntAsync(database.ConnectionString, "SELECT COUNT(*) FROM [Habito];"));
            Assert.True(await MigrationIsAppliedAsync(database.ConnectionString, TargetMigration));
            await AssertConcurrencyColumnAsync(database.ConnectionString);
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    private static TestHabit[] CreateHabits()
    {
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        return
        [
            new(Guid.NewGuid(), firstUserId, new DateOnly(2024, 2, 12), [DayOfWeek.Monday, DayOfWeek.Friday]),
            new(Guid.NewGuid(), firstUserId, new DateOnly(2025, 6, 3), [DayOfWeek.Tuesday]),
            new(Guid.NewGuid(), secondUserId, new DateOnly(2026, 3, 21), [DayOfWeek.Monday, DayOfWeek.Friday]),
            new(Guid.NewGuid(), secondUserId, new DateOnly(2026, 8, 1), [DayOfWeek.Saturday])
        ];
    }

    private static async Task SeedExistingDataAsync(string connectionString, IReadOnlyCollection<TestHabit> habits)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var userId in habits.Select(habit => habit.UserId).Distinct())
        {
            await ExecuteAsync(connection,
                "INSERT INTO [Usuario] ([Id], [Nombre], [Email], [PasswordHash], [TimeZoneId]) VALUES (@id, @name, @email, @passwordHash, @timeZoneId);",
                command =>
                {
                    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = userId;
                    command.Parameters.Add("@name", SqlDbType.NVarChar).Value = $"TEMP-004 {userId:N}";
                    command.Parameters.Add("@email", SqlDbType.NVarChar).Value = $"{userId:N}@example.test";
                    command.Parameters.Add("@passwordHash", SqlDbType.NVarChar).Value = "synthetic-test-hash";
                    command.Parameters.Add("@timeZoneId", SqlDbType.NVarChar, 100).Value = "Etc/UTC";
                });
        }

        foreach (var habit in habits)
        {
            await ExecuteAsync(connection,
                "INSERT INTO [Habito] ([Id], [Nombre], [Descripcion], [CreatedAt], [StartDate], [Estado], [UsuarioId]) VALUES (@id, @name, @description, @createdAt, @startDate, @state, @userId);",
                command =>
                {
                    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = habit.Id;
                    command.Parameters.Add("@name", SqlDbType.NVarChar).Value = $"Habit {habit.Id:N}";
                    command.Parameters.Add("@description", SqlDbType.NVarChar).Value = $"Description {habit.Id:N}";
                    command.Parameters.Add("@createdAt", SqlDbType.DateTimeOffset).Value =
                        new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
                    command.Parameters.Add("@startDate", SqlDbType.Date).Value = habit.StartDate.ToDateTime(TimeOnly.MinValue);
                    command.Parameters.Add("@state", SqlDbType.Int).Value = 0;
                    command.Parameters.Add("@userId", SqlDbType.UniqueIdentifier).Value = habit.UserId;
                });

            await ExecuteAsync(connection,
                "INSERT INTO [ProgramacionHabito] ([HabitoId], [VigenteDesde], [VigenteHasta]) VALUES (@habitId, @startDate, NULL);",
                command =>
                {
                    command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habit.Id;
                    command.Parameters.Add("@startDate", SqlDbType.Date).Value = habit.StartDate.ToDateTime(TimeOnly.MinValue);
                });

            foreach (var day in habit.Days)
            {
                await ExecuteAsync(connection,
                    "INSERT INTO [ProgramacionHabitoDia] ([HabitoId], [VigenteDesde], [Dia]) VALUES (@habitId, @startDate, @day);",
                    command =>
                    {
                        command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habit.Id;
                        command.Parameters.Add("@startDate", SqlDbType.Date).Value = habit.StartDate.ToDateTime(TimeOnly.MinValue);
                        command.Parameters.Add("@day", SqlDbType.Int).Value = (int)day;
                    });
            }

            await ExecuteAsync(connection,
                "INSERT INTO [Cumplimiento] ([Id], [Fecha], [HabitoId]) VALUES (@id, @date, @habitId);",
                command =>
                {
                    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
                    command.Parameters.Add("@date", SqlDbType.Date).Value = habit.StartDate.ToDateTime(TimeOnly.MinValue);
                    command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habit.Id;
                });
        }
    }

    private static async Task<HashSet<HabitSnapshot>> ReadHabitsAsync(string connectionString)
    {
        var results = new HashSet<HabitSnapshot>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT [Id], [Nombre], [Descripcion], [StartDate], [CreatedAt], [Estado], [UsuarioId] FROM [Habito] ORDER BY [Id];", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new HabitSnapshot(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                DateOnly.FromDateTime(reader.GetDateTime(3)),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetInt32(5),
                reader.GetGuid(6)));
        }

        return results;
    }

    private static async Task<HashSet<VersionedHabitSnapshot>> ReadVersionedHabitsAsync(string connectionString)
    {
        var results = new HashSet<VersionedHabitSnapshot>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT [Id], [Nombre], [Descripcion], [StartDate], [CreatedAt], [Estado], [UsuarioId], [ConcurrencyVersion] FROM [Habito] ORDER BY [Id];",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new VersionedHabitSnapshot(
                new HabitSnapshot(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    DateOnly.FromDateTime(reader.GetDateTime(3)),
                    reader.GetFieldValue<DateTimeOffset>(4),
                    reader.GetInt32(5),
                    reader.GetGuid(6)),
                reader.GetInt64(7)));
        }

        return results;
    }

    private static async Task<HashSet<ScheduleSnapshot>> ReadSchedulesAsync(string connectionString)
    {
        var results = new HashSet<ScheduleSnapshot>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT p.[HabitoId], p.[VigenteDesde], p.[VigenteHasta], d.[Dia] FROM [ProgramacionHabito] AS p INNER JOIN [ProgramacionHabitoDia] AS d ON d.[HabitoId] = p.[HabitoId] AND d.[VigenteDesde] = p.[VigenteDesde];",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ScheduleSnapshot(
                reader.GetGuid(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.GetInt32(3)));
        }

        return results;
    }

    private static async Task<HashSet<CompletionSnapshot>> ReadCompletionsAsync(string connectionString)
    {
        var results = new HashSet<CompletionSnapshot>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT [Id], [Fecha], [HabitoId] FROM [Cumplimiento];", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new CompletionSnapshot(
                reader.GetGuid(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                reader.GetGuid(2)));
        }

        return results;
    }

    private static async Task AssertConcurrencyColumnAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT t.[name], c.[is_nullable], c.[default_object_id] FROM sys.columns AS c INNER JOIN sys.types AS t ON t.[user_type_id] = c.[user_type_id] WHERE c.[object_id] = OBJECT_ID(N'[dbo].[Habito]') AND c.[name] = N'ConcurrencyVersion';",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Habito.ConcurrencyVersion must exist.");
        Assert.Equal("bigint", reader.GetString(0));
        Assert.False(reader.GetBoolean(1));
        Assert.Equal(0, reader.GetInt32(2));
        Assert.False(await reader.ReadAsync(), "The concurrency column must be unique in the catalog.");
    }

    private static async Task MigrateToAsync(string connectionString, string migration)
    {
        var options = new DbContextOptionsBuilder<ConstiaDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(60))
            .Options;
        await using var context = new ConstiaDbContext(options);
        await context.GetService<IMigrator>().MigrateAsync(migration);
    }

    private static async Task<DisposableDatabase> CreateDisposableDatabaseAsync(string adminConnectionString)
    {
        await WaitForSqlServerAsync(adminConnectionString);
        var databaseName = $"ConstiaTemp004Migration_{Guid.NewGuid():N}";
        await using (var connection = new SqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{databaseName}];", connection);
            await command.ExecuteNonQueryAsync();
        }

        var builder = new SqlConnectionStringBuilder(adminConnectionString) { InitialCatalog = databaseName };
        return new DisposableDatabase(adminConnectionString, builder.ConnectionString, databaseName);
    }

    private static async Task WaitForSqlServerAsync(string connectionString)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                return;
            }
            catch (SqlException) when (attempt < 59)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw new InvalidOperationException("The disposable TEMP-004 SQL Server service did not become ready.");
    }

    private static async Task DropDisposableDatabaseAsync(DisposableDatabase database)
    {
        await using var connection = new SqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"ALTER DATABASE [{database.Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database.Name}];",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string GetDisposableRunnerConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A disposable GitHub Actions TEMP-004 SQL Server connection is required.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (builder.DataSource != "127.0.0.1,1433"
            || builder.InitialCatalog != "master"
            || !string.Equals(builder.UserID, "sa", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(builder.Password))
        {
            throw new InvalidOperationException("TEMP-004 SQL tests require the isolated runner SQL Server endpoint.");
        }

        return builder.ConnectionString;
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        string sql,
        Action<SqlCommand> configure)
    {
        await using var command = new SqlCommand(sql, connection);
        configure(command);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarIntAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<bool> MigrationIsAppliedAsync(string connectionString, string migrationId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM [__EFMigrationsHistory] WHERE [MigrationId] = @migrationId;", connection);
        command.Parameters.Add("@migrationId", SqlDbType.NVarChar, 150).Value = migrationId;
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private sealed record TestHabit(Guid Id, Guid UserId, DateOnly StartDate, DayOfWeek[] Days);
    private sealed record HabitSnapshot(
        Guid Id,
        string Name,
        string? Description,
        DateOnly StartDate,
        DateTimeOffset CreatedAt,
        int State,
        Guid UserId);
    private sealed record ScheduleSnapshot(Guid HabitId, DateOnly StartDate, DateOnly? EndDate, int Day);
    private sealed record CompletionSnapshot(Guid Id, DateOnly Date, Guid HabitId);
    private sealed record VersionedHabitSnapshot(HabitSnapshot Habit, long ConcurrencyVersion);
    private sealed record DisposableDatabase(string AdminConnectionString, string ConnectionString, string Name);

    private sealed class SqlServerIntegrationFactAttribute : FactAttribute
    {
        public SqlServerIntegrationFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Environment.GetEnvironmentVariable(IntegrationEnabledEnvironmentVariable), "true", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
            {
                Skip = "Requires an explicitly enabled disposable TEMP-004 SQL Server runner.";
            }
        }
    }
}
