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

public sealed class HabCat02SqlServerTests
{
    private const string EnabledVariable = "CONSTIA_HABCAT02_SQLSERVER_TESTS";
    private const string ConnectionVariable = "CONSTIA_HABCAT02_SQLSERVER_CONNECTION";
    private const string ContainerVariable = "CONSTIA_HABCAT02_CONTAINER_ID";

    [HabCatSqlFact]
    public async Task AsignacionInicial_SeLeeDesdeOtroContexto()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (id, owner) = await SeedAsync(database);
        await using (var context = database.CreateContext())
        {
            var result = await Edit(context).EjecutarAsync(id, owner, Request("Leer", 4, "billete"));
            Assert.NotNull(result);
            Assert.Equal(CatalogoCategoriasHabito.Todas[4].Id, result.CategoriaId);
            Assert.Equal("billete", result.IconoId);
        }
        await using var verify = database.CreateContext();
        var habit = await Repo(verify).BuscarPorIdYUsuarioAsync(id, owner, default);
        Assert.NotNull(habit);
        Assert.Equal(CatalogoCategoriasHabito.Todas[4].Id, habit.CategoriaId);
        Assert.Equal("billete", habit.IconoId);
        Assert.Equal(10, await verify.CategoriasHabito.CountAsync());
    }

    [HabCatSqlFact]
    public async Task Reemplazo_ConservaFechasHistoriaYEstado()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (id, owner) = await SeedAsync(database);
        await using var context = database.CreateContext();
        var habit = await Repo(context).BuscarParaModificarPorIdYUsuarioAsync(id, owner, default);
        Assert.NotNull(habit);
        var created = habit.FechaCreacion;
        await Edit(context).EjecutarAsync(id, owner, Request("Leer", 0, "libro"));
        await Edit(context).EjecutarAsync(id, owner, Request("Leer", 4, "billete"));
        await using var verify = database.CreateContext();
        var saved = await Repo(verify).BuscarPorIdYUsuarioAsync(id, owner, default);
        Assert.NotNull(saved);
        Assert.Equal(CatalogoCategoriasHabito.Todas[4].Id, saved.CategoriaId);
        Assert.Equal("billete", saved.IconoId);
        Assert.Equal(created, saved.FechaCreacion);
        Assert.Equal(new DateOnly(2026,10,1), saved.FechaInicio);
        Assert.Equal(EstadoHabito.Activo, saved.Estado);
        Assert.Single(saved.Programaciones);
        Assert.Single(saved.PeriodosEstado);
        Assert.Equal(1, await verify.Set<Cumplimiento>().CountAsync());
    }

    [HabCatSqlFact]
    public async Task SoloMetadatos_IncrementaVersionSinVersionarDias()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (id, owner) = await SeedAsync(database);
        await using var context = database.CreateContext();
        var habit = await Repo(context).BuscarParaModificarPorIdYUsuarioAsync(id, owner, default);
        Assert.NotNull(habit);
        var before = context.Entry(habit).Property<long>("ConcurrencyVersion").CurrentValue;
        await Edit(context).EjecutarAsync(id, owner, Request("Leer", 4, "libro"));
        await using var verify = database.CreateContext();
        var saved = await Repo(verify).BuscarParaModificarPorIdYUsuarioAsync(id, owner, default);
        Assert.NotNull(saved);
        Assert.Equal(before + 1, verify.Entry(saved).Property<long>("ConcurrencyVersion").CurrentValue);
        Assert.Single(saved.Programaciones);
    }

    [HabCatSqlFact]
    public async Task DosContextos_UnGanadorYConflictoIdentificable()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (id, owner) = await SeedAsync(database);
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        await Repo(first).BuscarParaModificarPorIdYUsuarioAsync(id, owner, default);
        await Repo(second).BuscarParaModificarPorIdYUsuarioAsync(id, owner, default);
        await Edit(first).EjecutarAsync(id, owner, Request("Ganador", 0, "agua"));
        await Assert.ThrowsAsync<ConflictoEdicionConcurrenteException>(() =>
            Edit(second).EjecutarAsync(id, owner, Request("Perdedor", 4, "billete")));
        await using var verify = database.CreateContext();
        var saved = await Repo(verify).BuscarPorIdYUsuarioAsync(id, owner, default);
        Assert.NotNull(saved);
        Assert.Equal("Ganador", saved.Nombre);
        Assert.Equal("agua", saved.IconoId);
        Assert.Equal(CatalogoCategoriasHabito.Todas[0].Id, saved.CategoriaId);
    }

    [HabCatSqlFact]
    public async Task Conflicto_NoPersisteNombreMetadatosProgramacionNiCumplimientosParciales()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (id, owner) = await SeedAsync(database);
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstRepository = Repo(first);
        var secondRepository = Repo(second);
        var winner = await firstRepository.BuscarParaModificarPorIdYUsuarioAsync(id, owner, default);
        var loser = await secondRepository.BuscarParaModificarPorIdYUsuarioAsync(id, owner, default);
        Assert.NotNull(winner);
        Assert.NotNull(loser);
        var effectiveDate = new DateOnly(2026,10,10);
        winner.ActualizarConfiguracionVersionada("Ganador", null, [DayOfWeek.Friday], effectiveDate);
        winner.ActualizarMetadatos(CatalogoCategoriasHabito.Todas[0].Id, "agua");
        await firstRepository.GuardarCambiosAsync(winner, default);
        var before = await SnapshotAsync(database);

        // Simular dos ediciones ya cargadas: no recargar la gráfica del perdedor después del guardado ganador.
        loser.ActualizarConfiguracionVersionada("Perdedor", null, [DayOfWeek.Saturday], effectiveDate);
        loser.ActualizarMetadatos(CatalogoCategoriasHabito.Todas[4].Id, "billete");
        await Assert.ThrowsAsync<ConflictoEdicionConcurrenteException>(() =>
            secondRepository.GuardarCambiosAsync(loser, default));
        Assert.Equal(before, await SnapshotAsync(database));
    }

    [HabCatSqlFact]
    public async Task Categoria_FkSigueRestrictivaYExistenciaConsultaSoloLaIdentidad()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (id, owner) = await SeedAsync(database);
        await using var context = database.CreateContext();
        await Edit(context).EjecutarAsync(id, owner, Request("Leer", 4, "libro"));
        var categories = new CategoriaHabitoReadRepository(context);
        Assert.True(await categories.ExisteAsync(CatalogoCategoriasHabito.Todas[4].Id, default));
        Assert.False(await categories.ExisteAsync(Guid.Empty, default));
        var category = CatalogoCategoriasHabito.Todas[4].Id;
        var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [CategoriaHabito] WHERE [Id]={category}"));
        Assert.Equal(547, error.Number);
        Assert.Equal(10, await context.CategoriasHabito.CountAsync());
        var unknown = Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000099");
        var invalid = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Habito] SET [CategoriaId]={unknown} WHERE [Id]={id}"));
        Assert.Equal(547, invalid.Number);
        await using var verify = database.CreateContext();
        Assert.Equal(category, (await verify.Habitos.SingleAsync(h => h.Id == id)).CategoriaId);
    }

    [HabCatSqlFact]
    public async Task HistoricoNull_OmisionConservaNullDesdeOtroContexto()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        var (id, owner) = await SeedAsync(database);
        await using (var context = database.CreateContext())
        {
            await Edit(context).EjecutarAsync(id, owner, new EditarHabitoRequest("Leer", null, [DayOfWeek.Monday]));
        }
        await using var verify = database.CreateContext();
        var saved = await Repo(verify).BuscarPorIdYUsuarioAsync(id, owner, default);
        Assert.NotNull(saved);
        Assert.Null(saved.CategoriaId);
        Assert.Null(saved.IconoId);
        Assert.Single(saved.Programaciones);
        Assert.Single(saved.PeriodosEstado);
        Assert.Equal(1, await verify.Set<Cumplimiento>().CountAsync());
    }

    private static HabitoRepository Repo(ConstiaDbContext context) => new(context);
    private static EditarHabito Edit(ConstiaDbContext context) =>
        new(Repo(context), new UsuarioRepository(context), new LocalDate(), new CategoriaHabitoReadRepository(context));
    private sealed class LocalDate : IFechaLocalUsuario
    {
        public DateOnly ObtenerFechaLocal(DateTimeOffset instanteUtc, string timeZoneId) =>
            throw new NotSupportedException("Este fake solo prueba consumidores de ObtenerHoy.");

        public DateOnly ObtenerHoy(string zone) => new(2026,10,10);
    }
    private static EditarHabitoRequest Request(string name, int category, string icon) =>
        new(name, null, [DayOfWeek.Monday], CatalogoCategoriasHabito.Todas[category].Id, icon);
    private static async Task<(Guid Id, Guid Owner)> SeedAsync(DisposableDatabase database)
    {
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var owner = new Usuario("HAB-CAT-02", "habcat02@example.invalid", "test-hash", "Etc/UTC");
        var habit = HabitoHistorico.Crear(owner, "Leer", null, new DateOnly(2026,10,1), [DayOfWeek.Monday]);
        await Repo(context).AgregarAsync(habit, default);
        context.Add(new Cumplimiento(habit, habit.FechaInicio));
        await context.SaveChangesAsync();
        return (habit.Id, owner.Id);
    }
    private static async Task<string[]> SnapshotAsync(DisposableDatabase database)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var results = new List<string>();
        foreach (var table in new[] { "Habito", "ProgramacionHabito", "ProgramacionHabitoDia", "PeriodoEstadoHabito", "Cumplimiento" })
        {
            var order = table switch {
                "ProgramacionHabito" or "PeriodoEstadoHabito" => "[HabitoId],[VigenteDesde]",
                "ProgramacionHabitoDia" => "[HabitoId],[VigenteDesde],[Dia]",
                _ => "[Id]"
            };
            // Identificadores exclusivos de esta lista fija, nunca datos de entrada.
            await using var command = new SqlCommand($"SELECT * FROM [{table}] ORDER BY {order} FOR JSON PATH, INCLUDE_NULL_VALUES", connection);
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
            var database = new DisposableDatabase(admin, $"Constia_HabCat02_{Guid.NewGuid():N}");
            await using var connection = new SqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{database._name}];", connection);
            await command.ExecuteNonQueryAsync();
            return database;
        }

        public async ValueTask DisposeAsync()
        {
            if (!Regex.IsMatch(_name, "^Constia_HabCat02_[0-9a-f]{32}$")
                || new SqlConnectionStringBuilder(ConnectionString).InitialCatalog != _name)
                throw new InvalidOperationException("Refusing cleanup of a non-HAB-CAT-02 database.");
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
                throw new InvalidOperationException("HAB-CAT-02 requires its explicitly enabled disposable GitHub Actions service.");
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
            if (!IsEnabled()) Skip = "Requires the explicitly enabled disposable HAB-CAT-02 SQL Server CI service.";
        }
    }
}
