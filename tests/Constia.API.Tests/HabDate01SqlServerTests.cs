using System.Text.RegularExpressions;
using Constia.Application.Habitos;
using Constia.Application.Temporal;
using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Habitos;
using Constia.Infrastructure.Usuarios;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Constia.API.Tests;

public sealed class HabDate01SqlServerTests
{
    private const string EnabledVariable = "CONSTIA_HABDATE01_SQLSERVER_TESTS";
    private const string ConnectionVariable = "CONSTIA_HABDATE01_SQLSERVER_CONNECTION";
    private const string ContainerVariable = "CONSTIA_HABDATE01_CONTAINER_ID";
    private static readonly DateTimeOffset Instant = DateTimeOffset.Parse("2026-10-10T02:30:00Z");

    [HabDateSqlFact]
    public async Task Alta_PersisteMetadatosYVersionInicial()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (owner, _) = await SeedAsync(database);
        Guid id;
        await using (var context = database.CreateContext())
        {
            var result = await Create(context).EjecutarAsync(Request(owner));
            Assert.NotNull(result);
            id = result.Id;
        }
        await using var verify = database.CreateContext();
        var habit = await new HabitoRepository(verify).BuscarPorIdYUsuarioAsync(id, owner, default);
        Assert.NotNull(habit);
        Assert.Equal(CatalogoCategoriasHabito.Todas[4].Id, habit.CategoriaId);
        Assert.Equal("billete", habit.IconoId);
        Assert.Equal(1L, await verify.Habitos.Where(h => h.Id == id)
            .Select(h => EF.Property<long>(h, "ConcurrencyVersion")).SingleAsync());
        Assert.Equal(10, await verify.CategoriasHabito.CountAsync());
    }

    [HabDateSqlFact]
    public async Task Alta_UnInstanteUtcYFechaLocalEnTodaLaGrafica()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (owner, _) = await SeedAsync(database);
        Guid id;
        await using (var context = database.CreateContext())
        {
            var clock = new Clock();
            var result = await Create(context, clock).EjecutarAsync(Request(owner));
            Assert.NotNull(result);
            id = result.Id;
            Assert.Equal(1, clock.Reads);
        }
        await using var verify = database.CreateContext();
        var habit = await new HabitoRepository(verify).BuscarPorIdYUsuarioAsync(id, owner, default);
        Assert.NotNull(habit);
        Assert.Equal(Instant, habit.FechaCreacion);
        Assert.Equal(TimeSpan.Zero, habit.FechaCreacion.Offset);
        Assert.Equal(new DateOnly(2026, 10, 9), habit.FechaInicio);
        Assert.Equal(habit.FechaInicio, Assert.Single(habit.Programaciones).VigenteDesde);
        var period = Assert.Single(habit.PeriodosEstado);
        Assert.Equal(habit.FechaInicio, period.VigenteDesde);
        Assert.Equal(EstadoHabito.Activo, period.Estado);
        Assert.Equal(EstadoHabito.Activo, habit.Estado);
        Assert.Equal([DayOfWeek.Monday], habit.DiasProgramados.Select(d => d.Dia));
        Assert.NotEqual(DayOfWeek.Monday, habit.FechaInicio.DayOfWeek);
    }

    [HabDateSqlFact]
    public async Task Alta_NoReclasificaNiReescribeHistoriaExistente()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (owner, historical) = await SeedAsync(database);
        var before = await SnapshotAsync(database, historical);
        await using (var context = database.CreateContext())
            Assert.NotNull(await Create(context).EjecutarAsync(Request(owner)));
        Assert.Equal(before, await SnapshotAsync(database, historical));
        await using var verify = database.CreateContext();
        var historic = await new HabitoRepository(verify).BuscarPorIdYUsuarioAsync(historical, owner, default);
        Assert.NotNull(historic);
        Assert.Null(historic.CategoriaId);
        Assert.Null(historic.IconoId);
        Assert.Equal(new DateOnly(2020, 1, 1), historic.FechaInicio);
    }

    [HabDateSqlFact]
    public async Task Fk_RechazaReferenciaInvalidaSinPerdidaParcial()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (_, historical) = await SeedAsync(database);
        var before = await SnapshotAsync(database, historical);
        await using var context = database.CreateContext();
        var unknown = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var error = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Habito] SET [CategoriaId]={unknown} WHERE [Id]={historical}"));
        Assert.Equal(547, error.Number);
        Assert.Equal(before, await SnapshotAsync(database, historical));
    }

    [HabDateSqlFact]
    public async Task CategoriaOficialAusente_NoGuardaNingunaParteDelAgregado()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (owner, historical) = await SeedAsync(database);
        var before = await SnapshotAsync(database, historical);
        await using var context = database.CreateContext();
        var category = CatalogoCategoriasHabito.Todas[4].Id;
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [CategoriaHabito] WHERE [Id]={category}");
        var error = await Assert.ThrowsAsync<MetadatosHabitoInvalidosException>(() =>
            Create(context).EjecutarAsync(Request(owner)));
        Assert.Equal("categoriaId", error.Campo);
        Assert.Equal(before, await SnapshotAsync(database, historical));
        Assert.Equal(1, await context.Habitos.CountAsync());
        Assert.Equal(1, await context.Set<ProgramacionHabito>().CountAsync());
        Assert.Equal(1, await context.Set<PeriodoEstadoHabito>().CountAsync());
    }

    [HabDateSqlFact]
    public async Task IconoInvalido_NoGuardaNiModificaHistoricos()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (owner, historical) = await SeedAsync(database);
        var before = await SnapshotAsync(database, historical);
        await using var context = database.CreateContext();
        var error = await Assert.ThrowsAsync<MetadatosHabitoInvalidosException>(() =>
            Create(context).EjecutarAsync(Request(owner) with { IconoId = "Libro" }));
        Assert.Equal("iconoId", error.Campo);
        Assert.Equal(1, await context.Habitos.CountAsync());
        Assert.Equal(before, await SnapshotAsync(database, historical));
    }

    [HabDateSqlFact]
    public async Task SnapshotNoCapturado_BloqueaSinPersistenciaParcial()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (owner, historical) = await SeedAsync(database);
        var before = await SnapshotAsync(database, historical);
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync("UPDATE [InicializacionEstadoHabito] SET [SnapshotCapturedAtUtc]=NULL, [ExpectedLegacyHabitCount]=NULL WHERE [Id]=1");
        await Assert.ThrowsAsync<HistorialEstadoHabitoNoInicializadoException>(() =>
            Create(context).EjecutarAsync(Request(owner)));
        Assert.Equal(1, await context.Habitos.CountAsync());
        Assert.Equal(before, await SnapshotAsync(database, historical));
    }

    [HabDateSqlFact]
    public async Task Esquema_SinDefaultsBackfillNiNuevasMigraciones()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (owner, historical) = await SeedAsync(database);
        await using var context = database.CreateContext();
        var migrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.NotNull(await Create(context).EjecutarAsync(Request(owner)));
        Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
        Assert.EndsWith("_AddEmailVerification", migrations[^1]); // Baseline ampliada por Etapa B; la operación sigue sin generar migraciones.
        var defaults = await context.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.default_constraints d
            JOIN sys.columns c ON c.object_id=d.parent_object_id AND c.column_id=d.parent_column_id
            WHERE OBJECT_NAME(c.object_id)=N'Habito' AND c.name IN (N'CategoriaId',N'IconoId',N'StartDate',N'CreatedAt')
            """).SingleAsync();
        Assert.Equal(0, defaults);
        var historic = await context.Habitos.SingleAsync(h => h.Id == historical);
        Assert.Null(historic.CategoriaId);
        Assert.Null(historic.IconoId);
        Assert.Equal(new DateOnly(2020, 1, 1), historic.FechaInicio);
    }

    private static CrearHabitoRequest Request(Guid owner) =>
        new(owner, "Leer", null, CatalogoCategoriasHabito.Todas[4].Id, "billete", [DayOfWeek.Monday]);

    private sealed class Clock : TimeProvider
    {
        internal int Reads;
        public override DateTimeOffset GetUtcNow() => Instant.AddDays(Reads++);
    }

    private static CrearHabito Create(ConstiaDbContext context, Clock? clock = null)
    {
        clock ??= new Clock();
        return new CrearHabito(new UsuarioRepository(context), new HabitoRepository(context),
            new EstadoHabitoReadinessRepository(context), new CategoriaHabitoReadRepository(context),
            new FechaLocalUsuario(clock, new ZonaHorariaIana()), clock);
    }

    private static async Task<(Guid Owner, Guid Historical)> SeedAsync(DisposableDatabase database)
    {
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var user = new Usuario("HAB-DATE-01", "habdate01@example.invalid", "test-hash",
            "America/Argentina/Buenos_Aires");
        var historical = HabitoHistorico.Crear(user, "Histórico", null,
            new DateOnly(2020, 1, 1), [DayOfWeek.Monday]);
        await new HabitoRepository(context).AgregarAsync(historical, default);
        context.Add(new Cumplimiento(historical, new DateOnly(2020, 1, 6)));
        await context.SaveChangesAsync();
        // Captura realizada; el backfill global aún incompleto no impide las altas.
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO [InicializacionEstadoHabito]
                ([Id],[CutoverUtc],[SnapshotCapturedAtUtc],[ExpectedLegacyHabitCount],[CompletedAtUtc])
            VALUES (1,'2026-10-01T00:00:00+00:00','2026-10-01T00:01:00+00:00',1,NULL);
            """);
        return (user.Id, historical.Id);
    }

    private static async Task<string[]> SnapshotAsync(DisposableDatabase database, Guid historical)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var results = new List<string>();
        foreach (var table in new[] { "Habito", "ProgramacionHabito", "ProgramacionHabitoDia", "PeriodoEstadoHabito", "Cumplimiento" })
        {
            var key = table == "Habito" ? "Id" : "HabitoId";
            var order = table switch
            {
                "ProgramacionHabito" or "PeriodoEstadoHabito" => "[HabitoId],[VigenteDesde]",
                "ProgramacionHabitoDia" => "[HabitoId],[VigenteDesde],[Dia]",
                _ => "[Id]"
            };
            // Identificadores de la lista fija anterior; la identidad del hábito siempre es parametrizada.
            await using var command = new SqlCommand(
                $"SELECT * FROM [{table}] WHERE [{key}]=@id ORDER BY {order} FOR JSON PATH, INCLUDE_NULL_VALUES", connection);
            command.Parameters.AddWithValue("@id", historical);
            await using var reader = await command.ExecuteReaderAsync();
            var json = new System.Text.StringBuilder();
            while (await reader.ReadAsync()) json.Append(reader.GetString(0));
            results.Add(json.ToString());
        }
        return results.ToArray();
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
            var database = new DisposableDatabase(admin, $"Constia_HabDate01_{Guid.NewGuid():N}");
            await using var connection = new SqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{database._name}];", connection);
            await command.ExecuteNonQueryAsync();
            return database;
        }

        public async ValueTask DisposeAsync()
        {
            if (!Regex.IsMatch(_name, "^Constia_HabDate01_[0-9a-f]{32}$")
                || new SqlConnectionStringBuilder(ConnectionString).InitialCatalog != _name)
                throw new InvalidOperationException("Refusing cleanup of a non-HAB-DATE-01 database.");
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
                throw new InvalidOperationException("HAB-DATE-01 requires its explicitly enabled disposable GitHub Actions service.");
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

    private sealed class HabDateSqlFactAttribute : FactAttribute
    {
        public HabDateSqlFactAttribute()
        {
            if (!IsEnabled()) Skip = "Requires the explicitly enabled disposable HAB-DATE-01 SQL Server CI service.";
        }
    }
}
