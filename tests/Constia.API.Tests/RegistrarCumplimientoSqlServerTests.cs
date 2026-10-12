using System.Data;
using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Habitos;
using Constia.Infrastructure.Cumplimientos;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Constia.API.Tests;

public sealed class RegistrarCumplimientoSqlServerTests
{
    private const string EnableEnvironmentVariable = "CONSTIA_COMP004_SQLSERVER_TESTS";
    private const string ConnectionEnvironmentVariable = "CONSTIA_COMP004_SQLSERVER_CONNECTION";

    [SqlServerIntegrationFact]
    public async Task Repositorio_GuardaCumplimientoYRespetaLaForeignKey()
    {
        var adminConnectionString = GetDisposableRunnerConnectionString();
        var database = await CreateDisposableDatabaseAsync(adminConnectionString);

        try
        {
            await using (var context = CreateContext(database.ConnectionString))
            {
                await context.Database.MigrateAsync();
            }

            var userId = Guid.NewGuid();
            var habitId = Guid.NewGuid();
            var fecha = new DateOnly(2026, 10, 10);
            var existingCompletionId = Guid.NewGuid();
            var startDate = new DateOnly(2026, 10, 9);
            await InsertOwnerAndHabitAsync(database.ConnectionString, userId, habitId, startDate, existingCompletionId);

            await using (var context = CreateContext(database.ConnectionString))
            {
                var habits = new HabitoRepository(context);
                var habit = await habits.BuscarParaModificarPorIdYUsuarioAsync(
                    habitId,
                    userId,
                    CancellationToken.None);
                Assert.NotNull(habit);

                var completions = new CumplimientoRepository(context);
                Assert.False(await completions.ExistePorHabitoYFechaAsync(
                    habitId,
                    fecha,
                    CancellationToken.None));

                var completion = new Cumplimiento(habit, fecha);
                await completions.AgregarAsync(completion, CancellationToken.None);

                Assert.True(await completions.ExistePorHabitoYFechaAsync(
                    habitId,
                    fecha,
                    CancellationToken.None));
                Assert.Equal(EntityState.Unchanged, context.Entry(habit).State);
                Assert.Equal(1L, context.Entry(habit).Property<long>("ConcurrencyVersion").CurrentValue);
                Assert.Equal(2, await ScalarIntAsync(database.ConnectionString,
                    "SELECT COUNT(*) FROM [Cumplimiento];"));
                Assert.Equal(1, await ScalarIntAsync(database.ConnectionString,
                    "SELECT COUNT(*) FROM [Cumplimiento] WHERE [Id] = @id AND [Fecha] = @fecha AND [HabitoId] = @habitId;",
                    command =>
                    {
                        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = existingCompletionId;
                        command.Parameters.Add("@fecha", SqlDbType.Date).Value = startDate.ToDateTime(TimeOnly.MinValue);
                        command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habitId;
                    }));
                Assert.Equal(1, await ScalarIntAsync(database.ConnectionString,
                    "SELECT COUNT(*) FROM [Cumplimiento] WHERE [Id] = @id;",
                    command => command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = completion.Id));
                Assert.Equal(1, await ScalarIntAsync(database.ConnectionString,
                    "SELECT COUNT(*) FROM sys.foreign_keys AS fk INNER JOIN sys.foreign_key_columns AS fkc ON fkc.[constraint_object_id] = fk.[object_id] INNER JOIN sys.columns AS parentColumn ON parentColumn.[object_id] = fkc.[parent_object_id] AND parentColumn.[column_id] = fkc.[parent_column_id] INNER JOIN sys.columns AS referencedColumn ON referencedColumn.[object_id] = fkc.[referenced_object_id] AND referencedColumn.[column_id] = fkc.[referenced_column_id] WHERE fk.[name] = N'FK_Cumplimiento_Habito_HabitoId' AND fk.[referenced_object_id] = OBJECT_ID(N'[dbo].[Habito]') AND parentColumn.[name] = N'HabitoId' AND referencedColumn.[name] = N'Id';"));

                var invalidHabitId = Guid.NewGuid();
                var foreignKeyFailure = await Assert.ThrowsAsync<SqlException>(() =>
                    ExecuteAsync(database.ConnectionString,
                        "INSERT INTO [Cumplimiento] ([Id], [Fecha], [HabitoId]) VALUES (@id, @fecha, @habitId);",
                        command =>
                        {
                            command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
                            command.Parameters.Add("@fecha", SqlDbType.Date).Value = fecha.ToDateTime(TimeOnly.MinValue);
                            command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = invalidHabitId;
                        }));
                Assert.Equal(547, foreignKeyFailure.Number);
            }
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    private static ConstiaDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ConstiaDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(60))
            .Options;
        return new ConstiaDbContext(options);
    }

    private static async Task InsertOwnerAndHabitAsync(
        string connectionString,
        Guid userId,
        Guid habitId,
        DateOnly startDate,
        Guid existingCompletionId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection,
            "INSERT INTO [Usuario] ([Id], [Nombre], [Email], [PasswordHash], [TimeZoneId]) VALUES (@id, @name, @email, @hash, @timeZoneId);",
            command =>
            {
                command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = userId;
                command.Parameters.Add("@name", SqlDbType.NVarChar).Value = "COMP-004 SQL test";
                command.Parameters.Add("@email", SqlDbType.NVarChar).Value = $"{userId:N}@example.test";
                command.Parameters.Add("@hash", SqlDbType.NVarChar).Value = "synthetic-test-hash";
                command.Parameters.Add("@timeZoneId", SqlDbType.NVarChar, 100).Value = "Etc/UTC";
            });
        await ExecuteAsync(connection,
            "INSERT INTO [Habito] ([Id], [Nombre], [Descripcion], [CreatedAt], [StartDate], [Estado], [UsuarioId], [ConcurrencyVersion]) VALUES (@id, @name, NULL, @createdAt, @startDate, @state, @userId, 1);",
            command =>
            {
                command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = habitId;
                command.Parameters.Add("@name", SqlDbType.NVarChar).Value = "COMP-004 SQL habit";
                command.Parameters.Add("@createdAt", SqlDbType.DateTimeOffset).Value = DateTimeOffset.UtcNow;
                command.Parameters.Add("@startDate", SqlDbType.Date).Value = startDate.ToDateTime(TimeOnly.MinValue);
                command.Parameters.Add("@state", SqlDbType.Int).Value = (int)EstadoHabito.Activo;
                command.Parameters.Add("@userId", SqlDbType.UniqueIdentifier).Value = userId;
            });
        await ExecuteAsync(connection,
            "INSERT INTO [ProgramacionHabito] ([HabitoId], [VigenteDesde], [VigenteHasta]) VALUES (@habitId, @startDate, NULL);",
            command =>
            {
                command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habitId;
                command.Parameters.Add("@startDate", SqlDbType.Date).Value = startDate.ToDateTime(TimeOnly.MinValue);
            });
        foreach (var day in new[] { startDate.DayOfWeek, DayOfWeek.Saturday })
        {
            await ExecuteAsync(connection,
                "INSERT INTO [ProgramacionHabitoDia] ([HabitoId], [VigenteDesde], [Dia]) VALUES (@habitId, @startDate, @day);",
                command =>
                {
                    command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habitId;
                    command.Parameters.Add("@startDate", SqlDbType.Date).Value = startDate.ToDateTime(TimeOnly.MinValue);
                    command.Parameters.Add("@day", SqlDbType.Int).Value = (int)day;
                });
        }

        await ExecuteAsync(connection,
            "INSERT INTO [Cumplimiento] ([Id], [Fecha], [HabitoId]) VALUES (@id, @fecha, @habitId);",
            command =>
            {
                command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = existingCompletionId;
                command.Parameters.Add("@fecha", SqlDbType.Date).Value = startDate.ToDateTime(TimeOnly.MinValue);
                command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habitId;
            });
    }

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

    private static string GetDisposableRunnerConnectionString()
    {
        var value = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Environment.GetEnvironmentVariable(EnableEnvironmentVariable), "true", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("An explicitly enabled disposable COMP-004 SQL Server runner is required.");
        }

        var builder = new SqlConnectionStringBuilder(value);
        if (!string.Equals(builder.DataSource, "127.0.0.1,1433", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(builder.UserID, "sa", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(builder.Password))
        {
            throw new InvalidOperationException("COMP-004 SQL tests require the isolated runner SQL Server endpoint.");
        }

        return builder.ConnectionString;
    }

    private static async Task<DisposableDatabase> CreateDisposableDatabaseAsync(string adminConnectionString)
    {
        await WaitForSqlServerAsync(adminConnectionString);
        var name = $"ConstiaComp004_{Guid.NewGuid():N}";
        await using (var connection = new SqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{name}];", connection);
            await command.ExecuteNonQueryAsync();
        }

        var builder = new SqlConnectionStringBuilder(adminConnectionString) { InitialCatalog = name };
        return new DisposableDatabase(adminConnectionString, builder.ConnectionString, name);
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

        throw new InvalidOperationException("The disposable COMP-004 SQL Server service did not become ready.");
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

    private sealed record DisposableDatabase(
        string AdminConnectionString,
        string ConnectionString,
        string Name);

    private sealed class SqlServerIntegrationFactAttribute : FactAttribute
    {
        public SqlServerIntegrationFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Environment.GetEnvironmentVariable(EnableEnvironmentVariable), "true", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
            {
                Skip = "Requires an explicitly enabled disposable COMP-004 SQL Server runner.";
            }
        }
    }
}
