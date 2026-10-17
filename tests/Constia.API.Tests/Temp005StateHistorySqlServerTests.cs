using Constia.Application.Temporal;
using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Habitos;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Data;

namespace Constia.API.Tests;

public sealed class Temp005StateHistorySqlServerTests
{
    private const string ConnectionVariable = "CONSTIA_TEMP005_SQLSERVER_CONNECTION";
    private const string EnabledVariable = "CONSTIA_TEMP005_SQLSERVER_TESTS";

    [SqlServerIntegrationFact]
    public async Task Backfill_UsaSnapshotInmutableDeEstadoYZonaAunqueCambienDespuesDeLaCaptura()
    {
        var adminConnection = GetDisposableConnection();
        var database = await CreateDatabaseAsync(adminConnection);
        var userId = Guid.NewGuid();
        var habitId = Guid.NewGuid();
        var cutover = new DateTimeOffset(2026, 10, 16, 2, 30, 0, TimeSpan.Zero);

        try
        {
            await using (var context = CreateContext(database.ConnectionString))
            {
                await context.GetService<IMigrator>()
                    .MigrateAsync("20261011023329_AddHabitoConcurrencyVersion");
            }

            await SeedLegacyHabitAsync(database.ConnectionString, userId, habitId,
                new DateOnly(2024, 1, 10), "America/Argentina/Buenos_Aires", EstadoHabito.Inactivo);

            var businessDataBeforeMigration = await ReadBusinessDataAsync(database.ConnectionString);
            await using (var context = CreateContext(database.ConnectionString))
            {
                await context.Database.MigrateAsync();
            }

            var invalidStateError = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                database.ConnectionString,
                "INSERT INTO [PeriodoEstadoHabito] ([HabitoId], [VigenteDesde], [Estado], [VigenteHasta]) VALUES (@habitId, @since, 2, NULL);",
                command =>
                {
                    command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habitId;
                    command.Parameters.Add("@since", SqlDbType.Date).Value = new DateOnly(2026, 10, 15).ToDateTime(TimeOnly.MinValue);
                }));
            Assert.Equal(547, invalidStateError.Number);
            Assert.Equal(0, await ScalarIntAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM [PeriodoEstadoHabito] WHERE [HabitoId] = @habitId;",
                command => command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habitId));

            var businessDataAfterMigration = await ReadBusinessDataAsync(database.ConnectionString);
            AssertBusinessDataEqual(businessDataBeforeMigration, businessDataAfterMigration);

            await using (var context = CreateContext(database.ConnectionString))
            {
                var backfill = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);
                await backfill.CapturarSnapshotAsync(cutover, CancellationToken.None);
            }

            await ExecuteAsync(database.ConnectionString,
                "UPDATE [Usuario] SET [TimeZoneId] = N'Etc/UTC' WHERE [Id] = @userId;",
                command => command.Parameters.Add("@userId", SqlDbType.UniqueIdentifier).Value = userId);

            await using (var context = CreateContext(database.ConnectionString))
            {
                var backfill = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);
                await backfill.CapturarSnapshotAsync(cutover, CancellationToken.None);
                await backfill.AplicarBackfillAsync(CancellationToken.None);
            }

            await using var verify = CreateContext(database.ConnectionString);
            var period = await verify.Set<PeriodoEstadoHabito>().SingleAsync();
            var snapshot = await verify.Set<SnapshotEstadoHabito>().SingleAsync();
            var initialization = await verify.Set<InicializacionEstadoHabito>().SingleAsync();

            Assert.Equal(new DateOnly(2026, 10, 15), snapshot.FechaCorteLocal);
            Assert.Equal("America/Argentina/Buenos_Aires", snapshot.TimeZoneId);
            Assert.Equal(EstadoHabito.Inactivo, snapshot.EstadoAlCorte);
            Assert.Equal(new DateOnly(2026, 10, 15), period.VigenteDesde);
            Assert.Equal(EstadoHabito.Inactivo, period.Estado);
            Assert.Equal(cutover, initialization.CutoverUtc);
            Assert.NotNull(initialization.SnapshotCapturedAtUtc);
            Assert.NotNull(initialization.CompletedAtUtc);
            Assert.Equal(1, initialization.ExpectedLegacyHabitCount);
            Assert.Equal(EstadoHabito.Inactivo, await verify.Habitos
                .Where(habit => habit.Id == habitId).Select(habit => habit.Estado).SingleAsync());
            var businessDataAfterBackfill = await ReadBusinessDataAsync(database.ConnectionString);
            AssertBusinessDataEqual(businessDataBeforeMigration, businessDataAfterBackfill);
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Backfill_BaseVacia_MarcaSnapshotYFinalizacionConCeroHabitos()
    {
        var adminConnection = GetDisposableConnection();
        var database = await CreateDatabaseAsync(adminConnection);
        var cutover = new DateTimeOffset(2026, 10, 16, 2, 30, 0, TimeSpan.Zero);
        try
        {
            await using (var context = CreateContext(database.ConnectionString))
            {
                await context.Database.MigrateAsync();
                var backfill = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);
                await backfill.CapturarSnapshotAsync(cutover);
                await backfill.AplicarBackfillAsync();
            }

            await using var verify = CreateContext(database.ConnectionString);
            var marker = await verify.InicializacionesEstadoHabito.SingleAsync();
            Assert.Equal(0, marker.ExpectedLegacyHabitCount);
            Assert.NotNull(marker.SnapshotCapturedAtUtc);
            Assert.NotNull(marker.CompletedAtUtc);
            Assert.Empty(await verify.SnapshotsEstadoHabito.ToListAsync());
            Assert.Empty(await verify.Set<PeriodoEstadoHabito>().ToListAsync());
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Backfill_ReintentoReutilizaSnapshotYReanudaPeriodoYaAplicado()
    {
        var adminConnection = GetDisposableConnection();
        var database = await CreateDatabaseAsync(adminConnection);
        var userId = Guid.NewGuid();
        var firstHabitId = Guid.NewGuid();
        var secondHabitId = Guid.NewGuid();
        var cutover = new DateTimeOffset(2026, 10, 16, 2, 30, 0, TimeSpan.Zero);
        try
        {
            await using (var context = CreateContext(database.ConnectionString))
            {
                await context.Database.MigrateAsync();
            }

            await SeedLegacyHabitAsync(database.ConnectionString, userId, firstHabitId,
                new DateOnly(2024, 1, 10), "Etc/UTC", EstadoHabito.Activo);
            await SeedLegacyHabitAsync(database.ConnectionString, Guid.NewGuid(), secondHabitId,
                new DateOnly(2024, 2, 1), "Etc/UTC", EstadoHabito.Inactivo);

            await using (var context = CreateContext(database.ConnectionString))
            {
                var backfill = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);
                await backfill.CapturarSnapshotAsync(cutover);
            }

            // Simulate an item committed before an interrupted batch; the rerun must validate and skip it.
            await ExecuteAsync(database.ConnectionString,
                "INSERT INTO [PeriodoEstadoHabito] ([HabitoId], [Estado], [VigenteDesde], [VigenteHasta]) VALUES (@id, @state, @since, NULL);",
                command =>
                {
                    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = firstHabitId;
                    command.Parameters.Add("@state", SqlDbType.Int).Value = (int)EstadoHabito.Activo;
                    command.Parameters.Add("@since", SqlDbType.Date).Value = new DateOnly(2026, 10, 16).ToDateTime(TimeOnly.MinValue);
                });

            await using (var context = CreateContext(database.ConnectionString))
            {
                var backfill = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);
                await backfill.CapturarSnapshotAsync(cutover);
                await backfill.AplicarBackfillAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    backfill.CapturarSnapshotAsync(cutover.AddMinutes(1)));
            }

            await using var verify = CreateContext(database.ConnectionString);
            Assert.Equal(2, await verify.Set<PeriodoEstadoHabito>().CountAsync());
            Assert.NotNull((await verify.InicializacionesEstadoHabito.SingleAsync()).CompletedAtUtc);
            Assert.Equal(2, await verify.SnapshotsEstadoHabito.CountAsync());
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Backfill_EstadoVivoDistintoAlSnapshot_FallaSinReconstruirlo()
    {
        var adminConnection = GetDisposableConnection();
        var database = await CreateDatabaseAsync(adminConnection);
        var userId = Guid.NewGuid();
        var habitId = Guid.NewGuid();
        var cutover = new DateTimeOffset(2026, 10, 16, 2, 30, 0, TimeSpan.Zero);
        try
        {
            await using (var context = CreateContext(database.ConnectionString))
            {
                await context.Database.MigrateAsync();
            }

            await SeedLegacyHabitAsync(database.ConnectionString, userId, habitId,
                new DateOnly(2024, 1, 10), "Etc/UTC", EstadoHabito.Activo);
            await using (var context = CreateContext(database.ConnectionString))
            {
                await new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System)
                    .CapturarSnapshotAsync(cutover);
            }

            await ExecuteAsync(database.ConnectionString,
                "UPDATE [Habito] SET [Estado] = @state WHERE [Id] = @id;",
                command =>
                {
                    command.Parameters.Add("@state", SqlDbType.Int).Value = (int)EstadoHabito.Inactivo;
                    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = habitId;
                });

            await using (var context = CreateContext(database.ConnectionString))
            {
                var backfill = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);
                await Assert.ThrowsAsync<InvalidOperationException>(() => backfill.AplicarBackfillAsync());
            }

            Assert.Equal(0, await ScalarIntAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM [PeriodoEstadoHabito] WHERE [HabitoId] = @id;",
                command => command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = habitId));
            await using var verify = CreateContext(database.ConnectionString);
            Assert.Null((await verify.InicializacionesEstadoHabito.SingleAsync()).CompletedAtUtc);
            Assert.Equal(EstadoHabito.Activo, await verify.SnapshotsEstadoHabito
                .Where(item => item.HabitoId == habitId).Select(item => item.EstadoAlCorte).SingleAsync());
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Backfill_FechaInicioVivaDistintaAlSnapshot_FallaSinCrearPeriodo()
    {
        var adminConnection = GetDisposableConnection();
        var database = await CreateDatabaseAsync(adminConnection);
        var habitId = Guid.NewGuid();
        var cutover = new DateTimeOffset(2026, 10, 16, 2, 30, 0, TimeSpan.Zero);
        try
        {
            await using (var context = CreateContext(database.ConnectionString))
            {
                await context.Database.MigrateAsync();
            }

            await SeedLegacyHabitAsync(database.ConnectionString, Guid.NewGuid(), habitId,
                new DateOnly(2024, 1, 10), "Etc/UTC", EstadoHabito.Activo);
            await using (var context = CreateContext(database.ConnectionString))
            {
                await new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System)
                    .CapturarSnapshotAsync(cutover);
            }

            await ExecuteAsync(database.ConnectionString,
                "UPDATE [Habito] SET [StartDate] = @start WHERE [Id] = @id;",
                command =>
                {
                    command.Parameters.Add("@start", SqlDbType.Date).Value = new DateOnly(2024, 1, 11).ToDateTime(TimeOnly.MinValue);
                    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = habitId;
                });

            await using (var context = CreateContext(database.ConnectionString))
            {
                var backfill = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);
                await Assert.ThrowsAsync<InvalidOperationException>(() => backfill.AplicarBackfillAsync());
            }

            Assert.Equal(0, await ScalarIntAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM [PeriodoEstadoHabito] WHERE [HabitoId] = @id;",
                command => command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = habitId));
            await using var verify = CreateContext(database.ConnectionString);
            Assert.Null((await verify.InicializacionesEstadoHabito.SingleAsync()).CompletedAtUtc);
            Assert.Equal(new DateOnly(2024, 1, 10), await verify.SnapshotsEstadoHabito
                .Where(item => item.HabitoId == habitId).Select(item => item.FechaInicio).SingleAsync());
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Backfill_FechaLocalPersistidaNoDerivaDelCorteYZonaDelSnapshot_FallaSinCambios()
    {
        var adminConnection = GetDisposableConnection();
        var database = await CreateDatabaseAsync(adminConnection);
        var habitId = Guid.NewGuid();
        var cutover = new DateTimeOffset(2026, 10, 16, 2, 30, 0, TimeSpan.Zero);
        try
        {
            await using (var context = CreateContext(database.ConnectionString))
            {
                await context.Database.MigrateAsync();
            }

            await SeedLegacyHabitAsync(database.ConnectionString, Guid.NewGuid(), habitId,
                new DateOnly(2024, 1, 10), "America/Argentina/Buenos_Aires", EstadoHabito.Activo);
            await using (var context = CreateContext(database.ConnectionString))
            {
                await new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System)
                    .CapturarSnapshotAsync(cutover);
            }

            await ExecuteAsync(database.ConnectionString,
                "UPDATE [SnapshotEstadoHabito] SET [FechaCorteLocal] = @date WHERE [HabitoId] = @id;",
                command =>
                {
                    command.Parameters.Add("@date", SqlDbType.Date).Value = new DateOnly(2026, 10, 16).ToDateTime(TimeOnly.MinValue);
                    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = habitId;
                });

            await using (var context = CreateContext(database.ConnectionString))
            {
                var backfill = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);
                await Assert.ThrowsAsync<InvalidOperationException>(() => backfill.AplicarBackfillAsync());
            }

            Assert.Equal(0, await ScalarIntAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM [PeriodoEstadoHabito] WHERE [HabitoId] = @id;",
                command => command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = habitId));
            await using var verify = CreateContext(database.ConnectionString);
            Assert.Null((await verify.InicializacionesEstadoHabito.SingleAsync()).CompletedAtUtc);
            Assert.Equal(new DateOnly(2026, 10, 16), await verify.SnapshotsEstadoHabito
                .Where(item => item.HabitoId == habitId).Select(item => item.FechaCorteLocal).SingleAsync());
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Captura_ZonaIanaInvalida_DejaCorteFijoPeroNoSnapshotParcial()
    {
        var adminConnection = GetDisposableConnection();
        var database = await CreateDatabaseAsync(adminConnection);
        var cutover = new DateTimeOffset(2026, 10, 16, 2, 30, 0, TimeSpan.Zero);
        try
        {
            await using (var context = CreateContext(database.ConnectionString))
            {
                await context.Database.MigrateAsync();
            }

            await SeedLegacyHabitAsync(database.ConnectionString, Guid.NewGuid(), Guid.NewGuid(),
                new DateOnly(2024, 1, 10), "Invalid/Not-IANA", EstadoHabito.Activo);

            await using (var context = CreateContext(database.ConnectionString))
            {
                var backfill = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);
                await Assert.ThrowsAsync<ZonaHorariaPersistidaInvalidaException>(() =>
                    backfill.CapturarSnapshotAsync(cutover));
            }

            await using var verify = CreateContext(database.ConnectionString);
            var marker = await verify.InicializacionesEstadoHabito.SingleAsync();
            Assert.Equal(cutover, marker.CutoverUtc);
            Assert.Null(marker.SnapshotCapturedAtUtc);
            Assert.Null(marker.ExpectedLegacyHabitCount);
            Assert.Empty(await verify.SnapshotsEstadoHabito.ToListAsync());
            Assert.Empty(await verify.Set<PeriodoEstadoHabito>().ToListAsync());
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Backfill_CompletadoConDesactivacionPosterior_ReintentoNoModificaDatos()
    {
        var adminConnection = GetDisposableConnection();
        var database = await CreateDatabaseAsync(adminConnection);
        var userId = Guid.NewGuid();
        var habitId = Guid.NewGuid();
        var cutover = new DateTimeOffset(2026, 10, 16, 2, 30, 0, TimeSpan.Zero);
        try
        {
            await using (var context = CreateContext(database.ConnectionString))
            {
                await context.Database.MigrateAsync();
            }

            await SeedLegacyHabitAsync(database.ConnectionString, userId, habitId,
                new DateOnly(2024, 1, 10), "Etc/UTC", EstadoHabito.Activo);

            await using (var context = CreateContext(database.ConnectionString))
            {
                var backfill = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);
                await backfill.CapturarSnapshotAsync(cutover);
                await backfill.AplicarBackfillAsync();
            }

            await using (var context = CreateContext(database.ConnectionString))
            {
                var habit = await context.Habitos
                    .Include(item => item.PeriodosEstado)
                    .SingleAsync(item => item.Id == habitId);
                Assert.True(habit.Desactivar(new DateOnly(2026, 10, 17)));
                await new HabitoRepository(context).GuardarCambiosAsync(habit, CancellationToken.None);
            }

            var beforeRetry = await ReadBackfillDataAsync(database.ConnectionString);
            await using (var context = CreateContext(database.ConnectionString))
            {
                var backfill = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);
                var didApply = await backfill.AplicarBackfillAsync();
                Assert.False(didApply);
            }

            var afterRetry = await ReadBackfillDataAsync(database.ConnectionString);
            AssertBackfillDataEqual(beforeRetry, afterRetry);
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Backfill_MetadatosCompletadosContradictorios_FallaSinModificarDatos()
    {
        var adminConnection = GetDisposableConnection();
        var database = await CreateDatabaseAsync(adminConnection);
        var habitId = Guid.NewGuid();
        var cutover = new DateTimeOffset(2026, 10, 16, 2, 30, 0, TimeSpan.Zero);
        try
        {
            await using (var context = CreateContext(database.ConnectionString))
            {
                await context.Database.MigrateAsync();
            }

            await SeedLegacyHabitAsync(database.ConnectionString, Guid.NewGuid(), habitId,
                new DateOnly(2024, 1, 10), "Etc/UTC", EstadoHabito.Activo);

            await using (var context = CreateContext(database.ConnectionString))
            {
                var backfill = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);
                await backfill.CapturarSnapshotAsync(cutover);
                await backfill.AplicarBackfillAsync();
            }

            await ExecuteAsync(database.ConnectionString,
                "UPDATE [InicializacionEstadoHabito] SET [CompletedAtUtc] = DATEADD(second, -1, [SnapshotCapturedAtUtc]) WHERE [Id] = @id;",
                command => command.Parameters.Add("@id", SqlDbType.Int).Value = InicializacionEstadoHabito.RegistroUnicoId);

            var beforeRetry = await ReadBackfillDataAsync(database.ConnectionString);
            await using (var context = CreateContext(database.ConnectionString))
            {
                var backfill = new EstadoHabitoBackfillService(context, new ZonaHorariaIana(), TimeProvider.System);
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => backfill.AplicarBackfillAsync());
                Assert.Contains("metadatos de finalización", exception.Message, StringComparison.OrdinalIgnoreCase);
            }

            var afterRetry = await ReadBackfillDataAsync(database.ConnectionString);
            AssertBackfillDataEqual(beforeRetry, afterRetry);
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }
    private static ConstiaDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<ConstiaDbContext>().UseSqlServer(connectionString).Options);

    private static async Task SeedLegacyHabitAsync(
        string connectionString, Guid userId, Guid habitId, DateOnly start, string timeZoneId, EstadoHabito state)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection,
            "INSERT INTO [Usuario] ([Id], [Nombre], [Email], [PasswordHash], [TimeZoneId]) VALUES (@id, N'TEMP-005', @email, N'fake-hash', @zone);",
            command =>
            {
                command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = userId;
                command.Parameters.Add("@email", SqlDbType.NVarChar).Value = $"{userId:N}@example.test";
                command.Parameters.Add("@zone", SqlDbType.NVarChar, 100).Value = timeZoneId;
            });
        await ExecuteAsync(connection,
            "INSERT INTO [Habito] ([Id], [Nombre], [Descripcion], [CreatedAt], [StartDate], [Estado], [UsuarioId], [ConcurrencyVersion]) VALUES (@id, N'Leer', NULL, @created, @start, @state, @userId, 1);",
            command =>
            {
                command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = habitId;
                command.Parameters.Add("@created", SqlDbType.DateTimeOffset).Value = DateTimeOffset.UnixEpoch;
                command.Parameters.Add("@start", SqlDbType.Date).Value = start.ToDateTime(TimeOnly.MinValue);
                command.Parameters.Add("@state", SqlDbType.Int).Value = (int)state;
                command.Parameters.Add("@userId", SqlDbType.UniqueIdentifier).Value = userId;
            });
        await ExecuteAsync(connection,
            "INSERT INTO [ProgramacionHabito] ([HabitoId], [VigenteDesde], [VigenteHasta]) VALUES (@id, @start, NULL);",
            command =>
            {
                command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = habitId;
                command.Parameters.Add("@start", SqlDbType.Date).Value = start.ToDateTime(TimeOnly.MinValue);
            });
        await ExecuteAsync(connection,
            "INSERT INTO [ProgramacionHabitoDia] ([HabitoId], [VigenteDesde], [Dia]) VALUES (@id, @start, @day);",
            command =>
            {
                command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = habitId;
                command.Parameters.Add("@start", SqlDbType.Date).Value = start.ToDateTime(TimeOnly.MinValue);
                command.Parameters.Add("@day", SqlDbType.Int).Value = (int)start.DayOfWeek;
            });
        await ExecuteAsync(connection,
            "INSERT INTO [Cumplimiento] ([Id], [HabitoId], [Fecha]) VALUES (@id, @habitId, @date);",
            command =>
            {
                command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
                command.Parameters.Add("@habitId", SqlDbType.UniqueIdentifier).Value = habitId;
                command.Parameters.Add("@date", SqlDbType.Date).Value = start.ToDateTime(TimeOnly.MinValue);
            });
    }

    private static async Task<DisposableDatabase> CreateDatabaseAsync(string adminConnectionString)
    {
        var name = $"Constia_Temp005_{Guid.NewGuid():N}";
        await using var connection = new SqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"CREATE DATABASE [{name}];", connection);
        await command.ExecuteNonQueryAsync();
        var builder = new SqlConnectionStringBuilder(adminConnectionString) { InitialCatalog = name };
        return new DisposableDatabase(adminConnectionString, builder.ConnectionString, name);
    }

    private static async Task DropDatabaseAsync(DisposableDatabase database)
    {
        await using var connection = new SqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"ALTER DATABASE [{database.Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database.Name}];",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string GetDisposableConnection()
    {
        var value = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "true", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("TEMP-005 requires its explicitly enabled disposable GitHub Actions SQL Server.");
        }

        var builder = new SqlConnectionStringBuilder(value);
        if (builder.DataSource != "127.0.0.1,1433"
            || builder.InitialCatalog != "master"
            || !string.Equals(builder.UserID, "sa", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(builder.Password))
        {
            throw new InvalidOperationException("TEMP-005 tests only accept the disposable runner SQL Server endpoint.");
        }

        return builder.ConnectionString;
    }

    private static async Task ExecuteAsync(string connectionString, string sql, Action<SqlCommand> configure)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, sql, configure);
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

    private static async Task<BusinessData> ReadBusinessDataAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var schedules = new List<ScheduleRow>();
        await using (var command = new SqlCommand(
                         "SELECT [HabitoId], [VigenteDesde], [VigenteHasta] FROM [ProgramacionHabito] ORDER BY [HabitoId], [VigenteDesde];",
                         connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                schedules.Add(new ScheduleRow(
                    reader.GetGuid(0),
                    DateOnly.FromDateTime(reader.GetDateTime(1)),
                    reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2))));
            }
        }

        var days = new List<ScheduleDayRow>();
        await using (var command = new SqlCommand(
                         "SELECT [HabitoId], [VigenteDesde], [Dia] FROM [ProgramacionHabitoDia] ORDER BY [HabitoId], [VigenteDesde], [Dia];",
                         connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                days.Add(new ScheduleDayRow(reader.GetGuid(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetInt32(2)));
            }
        }

        var completions = new List<CompletionRow>();
        await using (var command = new SqlCommand(
                         "SELECT [Id], [HabitoId], [Fecha] FROM [Cumplimiento] ORDER BY [HabitoId], [Fecha], [Id];",
                         connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                completions.Add(new CompletionRow(reader.GetGuid(0), reader.GetGuid(1), DateOnly.FromDateTime(reader.GetDateTime(2))));
            }
        }

        return new BusinessData(schedules.ToArray(), days.ToArray(), completions.ToArray());
    }

    private static void AssertBusinessDataEqual(BusinessData expected, BusinessData actual)
    {
        Assert.Equal(expected.Schedules, actual.Schedules);
        Assert.Equal(expected.ScheduleDays, actual.ScheduleDays);
        Assert.Equal(expected.Completions, actual.Completions);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, Action<SqlCommand> configure)
    {
        await using var command = new SqlCommand(sql, connection);
        configure(command);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<BackfillData> ReadBackfillDataAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        var habits = await context.Habitos.AsNoTracking()
            .OrderBy(habit => habit.Id)
            .Select(habit => new HabitStateRow(
                habit.Id,
                habit.Estado,
                EF.Property<long>(habit, "ConcurrencyVersion")))
            .ToArrayAsync();
        var periods = await context.Set<PeriodoEstadoHabito>().AsNoTracking()
            .OrderBy(period => period.HabitoId)
            .ThenBy(period => period.VigenteDesde)
            .Select(period => new StatePeriodRow(
                period.HabitoId,
                period.Estado,
                period.VigenteDesde,
                period.VigenteHasta))
            .ToArrayAsync();
        var snapshots = await context.SnapshotsEstadoHabito.AsNoTracking()
            .OrderBy(snapshot => snapshot.HabitoId)
            .Select(snapshot => new SnapshotRow(
                snapshot.HabitoId,
                snapshot.TimeZoneId,
                snapshot.FechaCorteLocal,
                snapshot.FechaInicio,
                snapshot.EstadoAlCorte))
            .ToArrayAsync();
        var initializations = await context.InicializacionesEstadoHabito.AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => new InitializationRow(
                item.Id,
                item.CutoverUtc,
                item.SnapshotCapturedAtUtc,
                item.ExpectedLegacyHabitCount,
                item.CompletedAtUtc))
            .ToArrayAsync();

        return new BackfillData(
            await ReadBusinessDataAsync(connectionString),
            habits,
            periods,
            snapshots,
            initializations);
    }

    private static void AssertBackfillDataEqual(BackfillData expected, BackfillData actual)
    {
        AssertBusinessDataEqual(expected.Business, actual.Business);
        Assert.Equal(expected.Habits, actual.Habits);
        Assert.Equal(expected.Periods, actual.Periods);
        Assert.Equal(expected.Snapshots, actual.Snapshots);
        Assert.Equal(expected.Initializations, actual.Initializations);
    }
    private sealed record DisposableDatabase(string AdminConnectionString, string ConnectionString, string Name);

    private sealed record BackfillData(
        BusinessData Business,
        HabitStateRow[] Habits,
        StatePeriodRow[] Periods,
        SnapshotRow[] Snapshots,
        InitializationRow[] Initializations);

    private sealed record HabitStateRow(Guid HabitId, EstadoHabito State, long ConcurrencyVersion);

    private sealed record StatePeriodRow(Guid HabitId, EstadoHabito State, DateOnly StartsOn, DateOnly? EndsOn);

    private sealed record SnapshotRow(Guid HabitId, string TimeZoneId, DateOnly LocalCutover, DateOnly Start, EstadoHabito State);

    private sealed record InitializationRow(
        int Id,
        DateTimeOffset CutoverUtc,
        DateTimeOffset? SnapshotCapturedAtUtc,
        int? ExpectedLegacyHabitCount,
        DateTimeOffset? CompletedAtUtc);
    private sealed record BusinessData(ScheduleRow[] Schedules, ScheduleDayRow[] ScheduleDays, CompletionRow[] Completions);

    private sealed record ScheduleRow(Guid HabitId, DateOnly StartsOn, DateOnly? EndsOn);

    private sealed record ScheduleDayRow(Guid HabitId, DateOnly StartsOn, int Day);

    private sealed record CompletionRow(Guid Id, Guid HabitId, DateOnly Date);

    private sealed class SqlServerIntegrationFactAttribute : FactAttribute
    {
        public SqlServerIntegrationFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "true", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
            {
                Skip = "Requires the explicitly enabled disposable TEMP-005 SQL Server runner.";
            }
        }
    }
}
