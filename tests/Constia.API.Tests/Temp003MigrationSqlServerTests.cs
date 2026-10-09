using Constia.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Data;

namespace Constia.API.Tests;

public sealed class Temp003MigrationSqlServerTests
{
    private const string PreviousMigration = "20261008042613_AddUsuarioTimeZoneId";
    private const string TargetMigration = "20261009004634_VersionarProgramacionHabitos";
    private const string ConnectionEnvironmentVariable = "CONSTIA_TEMP003_SQLSERVER_CONNECTION";
    private const string IntegrationEnabledEnvironmentVariable = "CONSTIA_TEMP003_SQLSERVER_TESTS";

    [SqlServerIntegrationFact]
    public async Task Up_PreservaProgramacionesYCumplimientosDeHabitosExistentes()
    {
        var adminConnectionString = GetDisposableRunnerConnectionString();
        await WaitForSqlServerAsync(adminConnectionString);
        var database = CreateDatabaseConnectionString(adminConnectionString);

        await MigrateToAsync(database, PreviousMigration);
        var habits = DatosHabitos();
        await SeedExistingDataAsync(database, habits, incluirDias: true);

        var schedulesBefore = await ReadLegacySchedulesAsync(database);
        var completionsBefore = await ReadCompletionsAsync(database);

        await MigrateToAsync(database, TargetMigration);

        var versions = await ReadVersionsAsync(database);
        var schedulesAfter = await ReadVersionedSchedulesAsync(database);
        var completionsAfter = await ReadCompletionsAsync(database);

        Assert.Equal(habits.Length, versions.Count);
        Assert.Equal(habits.Length, versions.Select(version => version.HabitId).Distinct().Count());
        foreach (var habit in habits)
        {
            var version = Assert.Single(versions, item => item.HabitId == habit.Id);
            Assert.Equal(habit.StartDate, version.ValidFrom);
            Assert.Null(version.ValidTo);
        }

        Assert.Equal(schedulesBefore.Count, schedulesAfter.Count);
        Assert.Equal(schedulesAfter.Count, schedulesAfter.Distinct().Count());
        Assert.True(
            schedulesBefore.Select(item => new VersionedSchedule(item.HabitId, item.StartDate, item.Day))
                .ToHashSet()
                .SetEquals(schedulesAfter));
        Assert.True(completionsBefore.SetEquals(completionsAfter));

        Assert.False(await TableExistsAsync(database, "HabitoDiaProgramado"));
        Assert.True(await MigrationIsAppliedAsync(database, TargetMigration));
        await AssertIntegrityConstraintsAsync(database);
        await AssertVigenciaCheckIsEnforcedAsync(database, habits[0]);
    }

    [SqlServerIntegrationFact]
    public async Task Up_SinDiasProgramados_FallaCon51000YSinCambiosParciales()
    {
        var adminConnectionString = GetDisposableRunnerConnectionString();
        await WaitForSqlServerAsync(adminConnectionString);
        var database = CreateDatabaseConnectionString(adminConnectionString);
        var habit = DatosHabitos()[0];

        await MigrateToAsync(database, PreviousMigration);
        await SeedExistingDataAsync(database, [habit], incluirDias: false);
        var completionsBefore = await ReadCompletionsAsync(database);

        var exception = await Record.ExceptionAsync(() => MigrateToAsync(database, TargetMigration));
        var sqlException = FindSqlException(exception);

        Assert.NotNull(sqlException);
        Assert.Equal(51000, sqlException.Number);
        Assert.True(await TableExistsAsync(database, "HabitoDiaProgramado"));
        Assert.False(await TableExistsAsync(database, "ProgramacionHabito"));
        Assert.False(await TableExistsAsync(database, "ProgramacionHabitoDia"));
        Assert.False(await MigrationIsAppliedAsync(database, TargetMigration));
        Assert.Equal(1, await ScalarIntAsync(
            database,
            "SELECT COUNT(*) FROM [Usuario] WHERE [Id] = @userId;",
            command => command.Parameters.Add("@userId", SqlDbType.UniqueIdentifier).Value = habit.UserId));
        Assert.Equal(0, await ScalarIntAsync(
            database,
            "SELECT COUNT(*) FROM [HabitoDiaProgramado] WHERE [HabitoId] = @habitId;",
            command => command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habit.Id));
        Assert.Equal(1, await ScalarIntAsync(
            database,
            "SELECT COUNT(*) FROM [Habito] WHERE [Id] = @habitId;",
            command => command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habit.Id));
        Assert.True(completionsBefore.SetEquals(await ReadCompletionsAsync(database)));
    }

    private static async Task MigrateToAsync(string connectionString, string migration)
    {
        var options = new DbContextOptionsBuilder<ConstiaDbContext>()
            .UseSqlServer(connectionString, sqlServer => sqlServer.CommandTimeout(60))
            .Options;
        await using var dbContext = new ConstiaDbContext(options);
        await dbContext.GetService<IMigrator>().MigrateAsync(migration);
    }

    private static async Task SeedExistingDataAsync(
        string connectionString,
        IReadOnlyCollection<TestHabit> habits,
        bool incluirDias)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var user in habits.Select(habit => habit.UserId).Distinct())
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO [Usuario] ([Id], [Nombre], [Email], [PasswordHash], [TimeZoneId]) VALUES (@id, @name, @email, @passwordHash, @timeZoneId);",
                command =>
                {
                    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = user;
                    command.Parameters.Add("@name", SqlDbType.NVarChar).Value = $"Test {user:N}";
                    command.Parameters.Add("@email", SqlDbType.NVarChar).Value = $"{user:N}@example.test";
                    command.Parameters.Add("@passwordHash", SqlDbType.NVarChar).Value = "synthetic-test-hash";
                    command.Parameters.Add("@timeZoneId", SqlDbType.NVarChar, 100).Value = "Etc/UTC";
                });
        }

        foreach (var habit in habits)
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO [Habito] ([Id], [Nombre], [Descripcion], [CreatedAt], [StartDate], [Estado], [UsuarioId]) VALUES (@id, @name, @description, @createdAt, @startDate, @state, @userId);",
                command =>
                {
                    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = habit.Id;
                    command.Parameters.Add("@name", SqlDbType.NVarChar).Value = $"Habit {habit.Id:N}";
                    command.Parameters.Add("@description", SqlDbType.NVarChar).Value = DBNull.Value;
                    command.Parameters.Add("@createdAt", SqlDbType.DateTimeOffset).Value =
                        new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
                    command.Parameters.Add("@startDate", SqlDbType.Date).Value = habit.StartDate.ToDateTime(TimeOnly.MinValue);
                    command.Parameters.Add("@state", SqlDbType.Int).Value = 0;
                    command.Parameters.Add("@userId", SqlDbType.UniqueIdentifier).Value = habit.UserId;
                });

            if (incluirDias)
            {
                foreach (var day in habit.Days)
                {
                    await ExecuteAsync(
                        connection,
                        "INSERT INTO [HabitoDiaProgramado] ([HabitoId], [Dia]) VALUES (@habitId, @day);",
                        command =>
                        {
                            command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habit.Id;
                            command.Parameters.Add("@day", SqlDbType.Int).Value = (int)day;
                        });
                }
            }

            var completionId = Guid.NewGuid();
            await ExecuteAsync(
                connection,
                "INSERT INTO [Cumplimiento] ([Id], [Fecha], [HabitoId]) VALUES (@id, @date, @habitId);",
                command =>
                {
                    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = completionId;
                    command.Parameters.Add("@date", SqlDbType.Date).Value = habit.StartDate.ToDateTime(TimeOnly.MinValue);
                    command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habit.Id;
                });
        }
    }

    private static TestHabit[] DatosHabitos()
    {
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        return
        [
            new TestHabit(Guid.NewGuid(), firstUser, new DateOnly(2024, 1, 10), [DayOfWeek.Monday, DayOfWeek.Friday]),
            new TestHabit(Guid.NewGuid(), firstUser, new DateOnly(2025, 5, 20), [DayOfWeek.Tuesday]),
            new TestHabit(Guid.NewGuid(), secondUser, new DateOnly(2026, 3, 2), [DayOfWeek.Thursday, DayOfWeek.Saturday])
        ];
    }

    private static async Task<HashSet<LegacySchedule>> ReadLegacySchedulesAsync(string connectionString)
    {
        var results = new HashSet<LegacySchedule>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT h.[Id], h.[StartDate], d.[Dia] FROM [Habito] AS h INNER JOIN [HabitoDiaProgramado] AS d ON d.[HabitoId] = h.[Id];",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new LegacySchedule(
                reader.GetGuid(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                reader.GetInt32(2)));
        }

        return results;
    }

    private static async Task<List<ProgramVersion>> ReadVersionsAsync(string connectionString)
    {
        var results = new List<ProgramVersion>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT [HabitoId], [VigenteDesde], [VigenteHasta] FROM [ProgramacionHabito];",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ProgramVersion(
                reader.GetGuid(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2))));
        }

        return results;
    }

    private static async Task<List<VersionedSchedule>> ReadVersionedSchedulesAsync(string connectionString)
    {
        var results = new List<VersionedSchedule>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT [HabitoId], [VigenteDesde], [Dia] FROM [ProgramacionHabitoDia];",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new VersionedSchedule(
                reader.GetGuid(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                reader.GetInt32(2)));
        }

        return results;
    }

    private static async Task<HashSet<Completion>> ReadCompletionsAsync(string connectionString)
    {
        var results = new HashSet<Completion>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT [Id], [HabitoId], [Fecha] FROM [Cumplimiento];",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new Completion(
                reader.GetGuid(0),
                reader.GetGuid(1),
                DateOnly.FromDateTime(reader.GetDateTime(2))));
        }

        return results;
    }

    private static async Task AssertIntegrityConstraintsAsync(string connectionString)
    {
        Assert.Equal(1, await ScalarIntAsync(connectionString,
            "SELECT COUNT(*) FROM sys.key_constraints WHERE [name] = N'PK_ProgramacionHabito' AND [type] = 'PK';"));
        Assert.Equal(1, await ScalarIntAsync(connectionString,
            "SELECT COUNT(*) FROM sys.key_constraints WHERE [name] = N'PK_ProgramacionHabitoDia' AND [type] = 'PK';"));

        Assert.Equal(1, await ScalarIntAsync(connectionString,
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE [name] = N'FK_ProgramacionHabito_Habito_HabitoId' AND [is_disabled] = 0 AND [is_not_trusted] = 0 AND [delete_referential_action_desc] = N'NO_ACTION';"));
        Assert.Equal(1, await ScalarIntAsync(connectionString,
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE [name] = N'FK_ProgramacionHabitoDia_ProgramacionHabito_HabitoId_VigenteDesde' AND [is_disabled] = 0 AND [is_not_trusted] = 0 AND [delete_referential_action_desc] = N'CASCADE';"));
        Assert.Equal(1, await ScalarIntAsync(connectionString,
            "SELECT COUNT(*) FROM sys.check_constraints WHERE [name] = N'CK_ProgramacionHabito_Vigencia' AND [is_disabled] = 0 AND [is_not_trusted] = 0;"));

        var filter = await ScalarStringAsync(connectionString,
            "SELECT [filter_definition] FROM sys.indexes WHERE [name] = N'IX_ProgramacionHabito_HabitoId' AND [is_unique] = 1;");
        Assert.NotNull(filter);
        Assert.Contains("VigenteHasta", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IS NULL", filter, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertVigenciaCheckIsEnforcedAsync(string connectionString, TestHabit habit)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            connectionString,
            "INSERT INTO [ProgramacionHabito] ([HabitoId], [VigenteDesde], [VigenteHasta]) VALUES (@habitId, @date, @date);",
            command =>
            {
                command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habit.Id;
                command.Parameters.Add("@date", SqlDbType.Date).Value = habit.StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue);
            }));
        Assert.Equal(547, exception.Number);
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName) =>
        await ScalarIntAsync(
            connectionString,
            "SELECT CASE WHEN OBJECT_ID(QUOTENAME(@tableName), N'U') IS NULL THEN 0 ELSE 1 END;",
            command => command.Parameters.Add("@tableName", SqlDbType.NVarChar, 128).Value = tableName) == 1;

    private static async Task<bool> MigrationIsAppliedAsync(string connectionString, string migrationId) =>
        await ScalarIntAsync(
            connectionString,
            "SELECT COUNT(*) FROM [__EFMigrationsHistory] WHERE [MigrationId] = @migrationId;",
            command => command.Parameters.Add("@migrationId", SqlDbType.NVarChar, 150).Value = migrationId) == 1;

    private static async Task<int> ScalarIntAsync(
        string connectionString,
        string sql,
        Action<SqlCommand>? configure = null)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        configure?.Invoke(command);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string?> ScalarStringAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task ExecuteAsync(
        string connectionString,
        string sql,
        Action<SqlCommand> configure)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, sql, configure);
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

        throw new InvalidOperationException("The disposable SQL Server service did not become ready.");
    }

    private static string GetDisposableRunnerConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The disposable TEMP-003 SQL Server workflow connection is not configured.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (builder.DataSource != "127.0.0.1,1433" ||
            builder.InitialCatalog != "master" ||
            !string.Equals(builder.UserID, "sa", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(builder.Password))
        {
            throw new InvalidOperationException("The TEMP-003 integration tests require the isolated runner SQL Server endpoint.");
        }

        return builder.ConnectionString;
    }

    private static string CreateDatabaseConnectionString(string adminConnectionString)
    {
        var runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new InvalidOperationException("The disposable TEMP-003 workflow run identifier is not configured.");
        }

        var builder = new SqlConnectionStringBuilder(adminConnectionString)
        {
            InitialCatalog = $"ConstiaTemp003_{runId}_{Guid.NewGuid():N}"
        };
        return builder.ConnectionString;
    }

    private static SqlException? FindSqlException(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is SqlException sqlException)
            {
                return sqlException;
            }

            exception = exception.InnerException;
        }

        return null;
    }

    private sealed record TestHabit(Guid Id, Guid UserId, DateOnly StartDate, DayOfWeek[] Days);
    private sealed record LegacySchedule(Guid HabitId, DateOnly StartDate, int Day);
    private sealed record VersionedSchedule(Guid HabitId, DateOnly ValidFrom, int Day);
    private sealed record ProgramVersion(Guid HabitId, DateOnly ValidFrom, DateOnly? ValidTo);
    private sealed record Completion(Guid Id, Guid HabitId, DateOnly Date);

    private sealed class SqlServerIntegrationFactAttribute : FactAttribute
    {
        public SqlServerIntegrationFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Environment.GetEnvironmentVariable(IntegrationEnabledEnvironmentVariable), "true", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
            {
                Skip = "Requires the disposable TEMP-003 SQL Server service configured by its GitHub Actions workflow.";
            }
        }
    }
}
