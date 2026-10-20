using Constia.Application.Habitos;
using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Habitos;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Data;

namespace Constia.API.Tests;

public sealed class HabitoConcurrencySqlServerTests
{
    private const string ConnectionEnvironmentVariable = "CONSTIA_TEMP004_SQLSERVER_CONNECTION";
    private const string IntegrationEnabledEnvironmentVariable = "CONSTIA_TEMP004_SQLSERVER_TESTS";
    private const string RevisionProperty = "ConcurrencyVersion";

    [SqlServerIntegrationFact]
    public async Task Repository_InicializaEIncrementaVersionParaCambiosDeMetadatosDiasYSinCambiosEfectivos()
    {
        var database = await CrearBaseDescartableAsync();
        try
        {
            await MigrarHastaActualAsync(database.ConnectionString);
            var (usuario, habito) = CrearAgregado();
            await using (var context = CrearContexto(database.ConnectionString))
            {
                var repository = new HabitoRepository(context);
                await repository.AgregarAsync(habito, CancellationToken.None);

                Assert.Equal(1L, context.Entry(habito).Property<long>(RevisionProperty).CurrentValue);

                var tracked = await repository.BuscarParaModificarPorIdYUsuarioAsync(
                    habito.Id, usuario.Id, CancellationToken.None);
                Assert.NotNull(tracked);
                tracked.ActualizarConfiguracionVersionada(
                    "Nombre actualizado", "Descripción actualizada", [DayOfWeek.Monday], new DateOnly(2026, 10, 3));
                await repository.GuardarCambiosAsync(tracked, CancellationToken.None);
                Assert.Equal(2L, await ObtenerVersionAsync(context, habito.Id));

                tracked.ActualizarConfiguracionVersionada(
                    "Nombre actualizado", "Descripción actualizada", [DayOfWeek.Monday], new DateOnly(2026, 10, 4));
                await repository.GuardarCambiosAsync(tracked, CancellationToken.None);
                Assert.Equal(3L, await ObtenerVersionAsync(context, habito.Id));

                tracked.ActualizarConfiguracionVersionada(
                    "Nombre actualizado", "Descripción actualizada", [DayOfWeek.Friday], new DateOnly(2026, 10, 10));
                await repository.GuardarCambiosAsync(tracked, CancellationToken.None);
                Assert.Equal(4L, await ObtenerVersionAsync(context, habito.Id));
            }

            await using var verification = CrearContexto(database.ConnectionString);
            var persisted = await verification.Set<Habito>()
                .Include(item => item.Programaciones)
                .ThenInclude(programacion => programacion.DiasProgramados)
                .SingleAsync(item => item.Id == habito.Id);

            Assert.Equal([DayOfWeek.Friday], persisted.DiasProgramados.Select(day => day.Dia));
            Assert.Equal(2, persisted.Programaciones.Count);
            Assert.Equal(new DateOnly(2026, 10, 10),
                Assert.Single(persisted.Programaciones, programacion => programacion.VigenteHasta is null).VigenteDesde);
        }
        finally
        {
            await EliminarBaseDescartableAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Repository_DosContextosConLaMismaVersion_GanaUnoYRevierteCompletamenteElOtro()
    {
        var database = await CrearBaseDescartableAsync();
        try
        {
            await MigrarHastaActualAsync(database.ConnectionString);
            var (usuario, habito) = CrearAgregado();
            await using (var seedContext = CrearContexto(database.ConnectionString))
            {
                await new HabitoRepository(seedContext).AgregarAsync(habito, CancellationToken.None);
                seedContext.Add(new Cumplimiento(habito, new DateOnly(2026, 10, 7)));
                await seedContext.SaveChangesAsync();
            }

            await using var contextA = CrearContexto(database.ConnectionString);
            await using var contextB = CrearContexto(database.ConnectionString);
            var repositoryA = new HabitoRepository(contextA);
            var repositoryB = new HabitoRepository(contextB);
            var copyA = await repositoryA.BuscarParaModificarPorIdYUsuarioAsync(
                habito.Id, usuario.Id, CancellationToken.None);
            var copyB = await repositoryB.BuscarParaModificarPorIdYUsuarioAsync(
                habito.Id, usuario.Id, CancellationToken.None);
            Assert.NotNull(copyA);
            Assert.NotNull(copyB);

            var fechaEfectiva = new DateOnly(2026, 10, 10);
            copyA.ActualizarConfiguracionVersionada("Ganador A", null, [DayOfWeek.Friday], fechaEfectiva);
            copyB.ActualizarConfiguracionVersionada("Ganador B", null, [DayOfWeek.Saturday], fechaEfectiva);

            await repositoryA.GuardarCambiosAsync(copyA, CancellationToken.None);
            await Assert.ThrowsAsync<ConflictoEdicionConcurrenteException>(
                () => repositoryB.GuardarCambiosAsync(copyB, CancellationToken.None));

            await using var verification = CrearContexto(database.ConnectionString);
            var persisted = await verification.Set<Habito>()
                .Include(item => item.Programaciones)
                .ThenInclude(programacion => programacion.DiasProgramados)
                .SingleAsync(item => item.Id == habito.Id);
            var openVersion = Assert.Single(
                persisted.Programaciones,
                programacion => programacion.VigenteHasta is null);

            Assert.Equal("Ganador A", persisted.Nombre);
            Assert.Equal(2, persisted.Programaciones.Count);
            Assert.Equal([DayOfWeek.Friday], openVersion.DiasProgramados.Select(day => day.Dia));
            Assert.Equal(fechaEfectiva, openVersion.VigenteDesde);
            Assert.Equal(fechaEfectiva, Assert.Single(
                persisted.Programaciones,
                programacion => programacion.VigenteHasta == fechaEfectiva).VigenteHasta);
            Assert.Equal(1, await verification.Set<Cumplimiento>().CountAsync(completion => completion.Habito.Id == habito.Id));
            Assert.Equal(2L, await ObtenerVersionAsync(verification, habito.Id));
        }
        finally
        {
            await EliminarBaseDescartableAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Repository_ErrorDeCheckNoSeTraduceAConflictoYLaTransaccionRevierteLaRaiz()
    {
        var database = await CrearBaseDescartableAsync();
        try
        {
            await MigrarHastaActualAsync(database.ConnectionString);
            var (usuario, habito) = CrearAgregado();
            await using (var seedContext = CrearContexto(database.ConnectionString))
            {
                await new HabitoRepository(seedContext).AgregarAsync(habito, CancellationToken.None);
            }

            await using (var updateContext = CrearContexto(database.ConnectionString))
            {
                var repository = new HabitoRepository(updateContext);
                var tracked = await repository.BuscarParaModificarPorIdYUsuarioAsync(
                    habito.Id, usuario.Id, CancellationToken.None);
                Assert.NotNull(tracked);
                var version = Assert.Single(tracked.Programaciones);
                updateContext.Entry(version).Property(nameof(ProgramacionHabito.VigenteHasta)).CurrentValue =
                    version.VigenteDesde;

                await Assert.ThrowsAsync<DbUpdateException>(
                    () => repository.GuardarCambiosAsync(tracked, CancellationToken.None));
            }

            await using var verification = CrearContexto(database.ConnectionString);
            Assert.Equal(1L, await ObtenerVersionAsync(verification, habito.Id));
            var persistedVersion = await verification.Set<ProgramacionHabito>()
                .SingleAsync(programacion => EF.Property<Guid>(programacion, "HabitoId") == habito.Id);
            Assert.Null(persistedVersion.VigenteHasta);
        }
        finally
        {
            await EliminarBaseDescartableAsync(database);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Repository_DesactivacionYEdicionDeProgramacionCompitenConElMismoTokenSinCambiosParciales()
    {
        var database = await CrearBaseDescartableAsync();
        try
        {
            await MigrarHastaActualAsync(database.ConnectionString);
            var (usuario, habito) = CrearAgregado();
            var completionDate = new DateOnly(2026, 10, 7);
            await using (var seed = CrearContexto(database.ConnectionString))
            {
                var repository = new HabitoRepository(seed);
                await repository.AgregarAsync(habito, CancellationToken.None);
                seed.Add(new Cumplimiento(habito, completionDate));
                await seed.SaveChangesAsync();
            }

            await using var stateContext = CrearContexto(database.ConnectionString);
            await using var scheduleContext = CrearContexto(database.ConnectionString);
            var stateRepository = new HabitoRepository(stateContext);
            var scheduleRepository = new HabitoRepository(scheduleContext);
            var stateCopy = await stateRepository.BuscarParaModificarPorIdYUsuarioAsync(
                habito.Id, usuario.Id, CancellationToken.None);
            var scheduleCopy = await scheduleRepository.BuscarParaModificarPorIdYUsuarioAsync(
                habito.Id, usuario.Id, CancellationToken.None);
            Assert.NotNull(stateCopy);
            Assert.NotNull(scheduleCopy);

            var effectiveDate = new DateOnly(2026, 10, 10);
            Assert.True(stateCopy.Desactivar(effectiveDate));
            scheduleCopy.ActualizarConfiguracionVersionada(
                scheduleCopy.Nombre, scheduleCopy.Descripcion, [DayOfWeek.Friday], effectiveDate);

            await stateRepository.GuardarCambiosAsync(stateCopy, CancellationToken.None);
            await Assert.ThrowsAsync<ConflictoEdicionConcurrenteException>(
                () => scheduleRepository.GuardarCambiosAsync(scheduleCopy, CancellationToken.None));

            await using var verification = CrearContexto(database.ConnectionString);
            var persisted = await verification.Set<Habito>()
                .Include(item => item.PeriodosEstado)
                .Include(item => item.Programaciones)
                .ThenInclude(program => program.DiasProgramados)
                .SingleAsync(item => item.Id == habito.Id);
            var openState = Assert.Single(persisted.PeriodosEstado, period => period.VigenteHasta is null);
            var openSchedule = Assert.Single(persisted.Programaciones, program => program.VigenteHasta is null);

            Assert.Equal(EstadoHabito.Inactivo, persisted.Estado);
            Assert.Equal(EstadoHabito.Inactivo, openState.Estado);
            Assert.Equal(effectiveDate, openState.VigenteDesde);
            Assert.Single(persisted.Programaciones);
            Assert.Equal([DayOfWeek.Monday], openSchedule.DiasProgramados.Select(day => day.Dia));
            Assert.Equal(2L, await ObtenerVersionAsync(verification, habito.Id));
            Assert.Equal(1, await verification.Set<Cumplimiento>()
                .CountAsync(item => item.Habito.Id == habito.Id && item.Fecha == completionDate));
        }
        finally
        {
            await EliminarBaseDescartableAsync(database);
        }
    }

    private static (Usuario Usuario, Habito Habito) CrearAgregado()
    {
        var usuario = new Usuario("Concurrency test", $"{Guid.NewGuid():N}@example.test", "synthetic-test-hash", "Etc/UTC");
        var habito = HabitoHistorico.Crear(
            usuario,
            "Concurrency habit",
            null,
            new DateOnly(2026, 10, 1),
            [DayOfWeek.Monday]);
        return (usuario, habito);
    }

    private static async Task<long> ObtenerVersionAsync(ConstiaDbContext context, Guid habitId) =>
        await context.Set<Habito>()
            .Where(habito => habito.Id == habitId)
            .Select(habito => EF.Property<long>(habito, RevisionProperty))
            .SingleAsync();

    private static ConstiaDbContext CrearContexto(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ConstiaDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(60))
            .Options;
        return new ConstiaDbContext(options);
    }

    private static async Task MigrarHastaActualAsync(string connectionString)
    {
        await using var context = CrearContexto(connectionString);
        await context.GetService<IMigrator>().MigrateAsync();
    }

    private static async Task<DisposableDatabase> CrearBaseDescartableAsync()
    {
        var adminConnectionString = GetDisposableRunnerConnectionString();
        await EsperarSqlServerAsync(adminConnectionString);
        await using var connection = new SqlConnection(adminConnectionString);
        await connection.OpenAsync();
        var databaseName = $"ConstiaTemp004_{Guid.NewGuid():N}";
        await using (var command = new SqlCommand($"CREATE DATABASE [{databaseName}];", connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        var builder = new SqlConnectionStringBuilder(adminConnectionString) { InitialCatalog = databaseName };
        return new DisposableDatabase(adminConnectionString, builder.ConnectionString, databaseName);
    }

    private static async Task EsperarSqlServerAsync(string connectionString)
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

    private static async Task EliminarBaseDescartableAsync(DisposableDatabase database)
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
            throw new InvalidOperationException("A disposable GitHub Actions SQL Server connection is required.");
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
