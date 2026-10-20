using System.Data;
using Constia.Application.Cumplimientos;
using Constia.Application.Temporal;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Habitos;
using Constia.Infrastructure.Cumplimientos;
using Constia.Infrastructure.Usuarios;
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
                await SeedReadyStateHistoryAsync(context);
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
                Assert.Equal(1, await ScalarIntAsync(database.ConnectionString,
                    "SELECT COUNT(*) FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Cumplimiento]') AND [name] = N'UX_Cumplimiento_HabitoId_Fecha' AND [is_unique] = 1;"));

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

    [SqlServerIntegrationFact]
    public async Task RegistrarCumplimiento_DosSolicitudesSimultaneasParaLaMismaFecha_SoloUnaSeGuarda()
    {
        var database = await CreateReadyDatabaseAsync();
        try
        {
            var userId = Guid.NewGuid();
            var habitId = Guid.NewGuid();
            var today = new DateOnly(2026, 10, 17);
            await InsertOwnerAndHabitAsync(database.ConnectionString, userId, habitId, today.AddDays(-1), null);

            var barrier = new TwoPartyInsertBarrier();
            async Task<bool> TryRegisterAsync()
            {
                await using var context = CreateContext(database.ConnectionString);
                var useCase = new RegistrarCumplimiento(
                    new UsuarioRepository(context),
                    new HabitoRepository(context),
                    new BarrierCumplimientoRepository(new CumplimientoRepository(context), barrier),
                    new FixedLocalDate(today),
                    new EstadoHabitoReadinessRepository(context));
                try
                {
                    return await useCase.EjecutarAsync(habitId, userId, today) is not null;
                }
                catch (ConflictoRegistroCumplimientoException)
                {
                    return false;
                }
            }

            var results = await Task.WhenAll(TryRegisterAsync(), TryRegisterAsync());

            Assert.Single(results, result => result);
            Assert.Single(results, result => !result);
            Assert.Equal(1, await ScalarIntAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM [Cumplimiento] WHERE [HabitoId] = @habitId AND [Fecha] = @fecha;",
                command =>
                {
                    command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habitId;
                    command.Parameters.Add("@fecha", SqlDbType.Date).Value = today.ToDateTime(TimeOnly.MinValue);
                }));
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task RegistrarCumplimiento_PermiteHabitosYFechasDistintos()
    {
        var database = await CreateReadyDatabaseAsync();
        try
        {
            var today = new DateOnly(2026, 10, 17);
            var userId = Guid.NewGuid();
            var firstHabitId = Guid.NewGuid();
            var secondUserId = Guid.NewGuid();
            var secondHabitId = Guid.NewGuid();
            await InsertOwnerAndHabitAsync(database.ConnectionString, userId, firstHabitId, today.AddDays(-1), null);
            await InsertOwnerAndHabitAsync(database.ConnectionString, secondUserId, secondHabitId, today.AddDays(-1), null);

            foreach (var (ownerId, habitId, date) in new[]
                     {
                         (userId, firstHabitId, today.AddDays(-1)),
                         (userId, firstHabitId, today),
                         (secondUserId, secondHabitId, today)
                     })
            {
                await using var context = CreateContext(database.ConnectionString);
                var useCase = new RegistrarCumplimiento(
                    new UsuarioRepository(context),
                    new HabitoRepository(context),
                    new CumplimientoRepository(context),
                    new FixedLocalDate(today),
                    new EstadoHabitoReadinessRepository(context));

                Assert.NotNull(await useCase.EjecutarAsync(habitId, ownerId, date));
            }

            Assert.Equal(3, await ScalarIntAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM [Cumplimiento];"));
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Repositorio_ErrorDeClavePrimariaNoSeTraduceAConflictoDeDuplicado()
    {
        var database = await CreateReadyDatabaseAsync();
        try
        {
            var userId = Guid.NewGuid();
            var habitId = Guid.NewGuid();
            var today = new DateOnly(2026, 10, 17);
            await InsertOwnerAndHabitAsync(database.ConnectionString, userId, habitId, today.AddDays(-1), null);
            Guid completionId;

            await using (var context = CreateContext(database.ConnectionString))
            {
                var habit = await new HabitoRepository(context).BuscarParaModificarPorIdYUsuarioAsync(
                    habitId,
                    userId,
                    CancellationToken.None);
                Assert.NotNull(habit);
                var first = new Cumplimiento(habit, today);
                await new CumplimientoRepository(context).AgregarAsync(first, CancellationToken.None);
                completionId = first.Id;
            }

            await using (var context = CreateContext(database.ConnectionString))
            {
                var habit = await new HabitoRepository(context).BuscarParaModificarPorIdYUsuarioAsync(
                    habitId,
                    userId,
                    CancellationToken.None);
                Assert.NotNull(habit);
                var second = new Cumplimiento(habit, today.AddDays(-1));
                typeof(Cumplimiento).GetProperty(nameof(Cumplimiento.Id))!.SetValue(second, completionId);

                var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                    new CumplimientoRepository(context).AgregarAsync(second, CancellationToken.None));

                var sqlFailure = Assert.IsType<SqlException>(failure.InnerException);
                Assert.Equal(2627, sqlFailure.Number);
                Assert.DoesNotContain("UX_Cumplimiento_HabitoId_Fecha", sqlFailure.Message, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(1, await ScalarIntAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM [Cumplimiento] WHERE [HabitoId] = @habitId AND [Fecha] = @fecha;",
                command =>
                {
                    command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habitId;
                    command.Parameters.Add("@fecha", SqlDbType.Date).Value = today.ToDateTime(TimeOnly.MinValue);
                }));
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Migracion_ConDuplicadosPreviosFallaSinBorrarRegistros()
    {
        var adminConnectionString = GetDisposableRunnerConnectionString();
        var database = await CreateDisposableDatabaseAsync(adminConnectionString);
        try
        {
            const string lastPreComp005Migration = "20261011023329_AddHabitoConcurrencyVersion";
            await using (var context = CreateContext(database.ConnectionString))
            {
                await context.GetService<IMigrator>().MigrateAsync(lastPreComp005Migration);
            }

            var userId = Guid.NewGuid();
            var habitId = Guid.NewGuid();
            var startDate = new DateOnly(2026, 10, 16);
            await InsertOwnerAndHabitAsync(database.ConnectionString, userId, habitId, startDate, Guid.NewGuid());
            await ExecuteAsync(database.ConnectionString,
                "INSERT INTO [Cumplimiento] ([Id], [Fecha], [HabitoId]) VALUES (@id, @fecha, @habitId);",
                command =>
                {
                    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
                    command.Parameters.Add("@fecha", SqlDbType.Date).Value = startDate.ToDateTime(TimeOnly.MinValue);
                    command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habitId;
                });

            await using (var context = CreateContext(database.ConnectionString))
            {
                var migrationFailure = await Assert.ThrowsAsync<SqlException>(
                    () => context.GetService<IMigrator>().MigrateAsync());
                Assert.Equal(51005, migrationFailure.Number);
            }

            Assert.Equal(2, await ScalarIntAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM [Cumplimiento] WHERE [HabitoId] = @habitId AND [Fecha] = @fecha;",
                command =>
                {
                    command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habitId;
                    command.Parameters.Add("@fecha", SqlDbType.Date).Value = startDate.ToDateTime(TimeOnly.MinValue);
                }));
            Assert.Equal(0, await ScalarIntAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261016172555_AddCumplimientoUniqueHabitDate';"));
            Assert.Equal(0, await ScalarIntAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Cumplimiento]') AND [name] = N'UX_Cumplimiento_HabitoId_Fecha';"));
        }
        finally
        {
            await DropDisposableDatabaseAsync(database);
        }
    }

    private static async Task<DisposableDatabase> CreateReadyDatabaseAsync()
    {
        var database = await CreateDisposableDatabaseAsync(GetDisposableRunnerConnectionString());
        try
        {
            await using var context = CreateContext(database.ConnectionString);
            await context.Database.MigrateAsync();
            await SeedReadyStateHistoryAsync(context);
            return database;
        }
        catch
        {
            await DropDisposableDatabaseAsync(database);
            throw;
        }
    }

    private static ConstiaDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ConstiaDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(60))
            .Options;
        return new ConstiaDbContext(options);
    }

    private static async Task SeedReadyStateHistoryAsync(ConstiaDbContext context)
    {
        var marker = new InicializacionEstadoHabito(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero));
        marker.MarcarSnapshotCapturado(new DateTimeOffset(2026, 10, 16, 0, 1, 0, TimeSpan.Zero), 0);
        marker.MarcarCompletado(new DateTimeOffset(2026, 10, 16, 0, 2, 0, TimeSpan.Zero));
        context.InicializacionesEstadoHabito.Add(marker);
        await context.SaveChangesAsync();
    }

    private static async Task InsertOwnerAndHabitAsync(
        string connectionString,
        Guid userId,
        Guid habitId,
        DateOnly startDate,
        Guid? existingCompletionId)
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
            "IF OBJECT_ID(N'[dbo].[PeriodoEstadoHabito]', N'U') IS NOT NULL INSERT INTO [PeriodoEstadoHabito] ([HabitoId], [VigenteDesde], [VigenteHasta], [Estado]) VALUES (@habitId, @startDate, NULL, @state);",
            command =>
            {
                command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habitId;
                command.Parameters.Add("@startDate", SqlDbType.Date).Value = startDate.ToDateTime(TimeOnly.MinValue);
                command.Parameters.Add("@state", SqlDbType.Int).Value = (int)EstadoHabito.Activo;
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

        if (existingCompletionId is Guid completionId)
        {
            await ExecuteAsync(connection,
                "INSERT INTO [Cumplimiento] ([Id], [Fecha], [HabitoId]) VALUES (@id, @fecha, @habitId);",
                command =>
                {
                    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = completionId;
                    command.Parameters.Add("@fecha", SqlDbType.Date).Value = startDate.ToDateTime(TimeOnly.MinValue);
                    command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habitId;
                });
        }
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

    private sealed class TwoPartyInsertBarrier
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) == 2)
            {
                _release.TrySetResult();
            }

            await _release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    private sealed class BarrierCumplimientoRepository(
        ICumplimientoRepository inner,
        TwoPartyInsertBarrier barrier) : ICumplimientoRepository
    {
        public Task<bool> ExistePorHabitoYFechaAsync(
            Guid habitId,
            DateOnly fecha,
            CancellationToken cancellationToken) =>
            inner.ExistePorHabitoYFechaAsync(habitId, fecha, cancellationToken);

        public async Task AgregarAsync(Cumplimiento cumplimiento, CancellationToken cancellationToken)
        {
            await barrier.WaitAsync(cancellationToken);
            await inner.AgregarAsync(cumplimiento, cancellationToken);
        }
    }

    private sealed class FixedLocalDate(DateOnly today) : IFechaLocalUsuario
    {
        public DateOnly ObtenerFechaLocal(DateTimeOffset instanteUtc, string timeZoneId) =>
            throw new NotSupportedException("Este fake solo prueba consumidores de ObtenerHoy.");

        public DateOnly ObtenerHoy(string timeZoneId) => today;
    }

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
