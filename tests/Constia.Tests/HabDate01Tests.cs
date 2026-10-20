using Constia.Application.Habitos;
using Constia.Application.Temporal;
using Constia.Domain;

namespace Constia.Tests;

public partial class CrearHabitoTests
{

    private sealed class Readiness : IEstadoHabitoReadinessRepository
    {
        internal CancellationToken Token;
        public Task<bool> SnapshotCapturadoAsync(CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            return Task.FromResult(true);
        }
        public Task<bool> HistorialCompletoAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Crear no debe exigir historial completo.");
    }

    [Fact]
    public async Task SolicitudNullYPropietarioAusente_NoLeenRelojNiGuardan()
    {
        var clock = new Reloj();
        var repo = new FakeHabitoRepository();
        var categories = new Categorias();
        var useCase = new CrearHabito(new FakeUsuarioRepository(null), repo,
            new Readiness(), categories, new FechaLocalUsuario(clock, new ZonaHorariaIana()), clock);
        await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.EjecutarAsync(null!));
        Assert.Null(await useCase.EjecutarAsync(CrearSolicitud()));
        Assert.Equal(0, repo.CantidadDeInserciones);
        Assert.Equal(0, categories.Consultas);
        Assert.Equal(0, clock.Lecturas);
    }

    [Fact]
    public async Task CancellationToken_SePropagaATodasLasOperacionesSinExigirHistorialCompleto()
    {
        var user = CrearUsuario();
        var users = new FakeUsuarioRepository(user);
        var repo = new FakeHabitoRepository();
        var categories = new Categorias();
        var readiness = new Readiness();
        var clock = new Reloj();
        using var cancellation = new CancellationTokenSource();
        var useCase = new CrearHabito(users, repo, readiness, categories,
            new FechaLocalUsuario(clock, new ZonaHorariaIana()), clock);
        Assert.NotNull(await useCase.EjecutarAsync(CrearSolicitud(user.Id), cancellation.Token));
        Assert.Equal(cancellation.Token, users.CancellationTokenRecibido);
        Assert.Equal(cancellation.Token, repo.CancellationTokenRecibido);
        Assert.Equal(cancellation.Token, categories.Token);
        Assert.Equal(cancellation.Token, readiness.Token);
        Assert.Equal(1, repo.CantidadDeInserciones);
    }

    private sealed class Reloj : TimeProvider
    {
        internal DateTimeOffset Instante = DateTimeOffset.Parse("2026-10-10T02:30:00Z");
        internal int Lecturas;
        public override DateTimeOffset GetUtcNow() => Instante.AddDays(Lecturas++);
    }

    private sealed class Categorias(bool existe = true) : ICategoriaHabitoReadRepository
    {
        internal CancellationToken Token;
        internal int Consultas;
        public Task<bool> ExisteAsync(Guid id, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Consultas++;
            return Task.FromResult(existe);
        }
        public Task<IReadOnlyList<CategoriaHabitoListado>> ListarAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    [Theory]
    [InlineData("America/Argentina/Buenos_Aires", "2026-10-10T02:30:00Z", "2026-10-09")]
    [InlineData("Asia/Tokyo", "2026-10-10T23:30:00Z", "2026-10-11")]
    [InlineData("Etc/UTC", "2026-10-10T23:30:00Z", "2026-10-10")]
    [InlineData("America/New_York", "2026-03-08T06:59:59Z", "2026-03-08")]
    [InlineData("America/New_York", "2026-03-08T07:00:00Z", "2026-03-08")]
    [InlineData("America/Argentina/Buenos_Aires", "2026-10-10T03:00:00Z", "2026-10-10")]
    [InlineData("Etc/UTC", "2026-10-18T12:00:00+02:00", "2026-10-18")]
    public async Task Creacion_UsaUnSoloInstanteYLaZonaPersistida(string zone, string utc, string date)
    {
        var user = new Usuario("Test", "date@example.invalid", "hash", zone);
        var repository = new FakeHabitoRepository();
        var categories = new Categorias();
        var clock = new Reloj { Instante = DateTimeOffset.Parse(utc) };
        var useCase = new CrearHabito(new FakeUsuarioRepository(user), repository,
            new EstadoHabitoReadinessRepositoryFalso(historialCompleto: false), categories,
            new FechaLocalUsuario(clock, new ZonaHorariaIana()), clock);
        using var cancellation = new CancellationTokenSource();
        var request = CrearSolicitud(user.Id) with { IconoId = "billete" };
        var result = await useCase.EjecutarAsync(request, cancellation.Token);
        Assert.NotNull(result);
        var habit = Assert.IsType<Habito>(repository.HabitoPersistido);
        Assert.Equal(1, clock.Lecturas);
        Assert.Equal(clock.Instante.ToUniversalTime(), habit.FechaCreacion);
        Assert.Equal(TimeSpan.Zero, habit.FechaCreacion.Offset);
        Assert.Equal(DateOnly.Parse(date), habit.FechaInicio);
        Assert.Equal(habit.FechaInicio, Assert.Single(habit.Programaciones).VigenteDesde);
        var period = Assert.Single(habit.PeriodosEstado);
        Assert.Equal(habit.FechaInicio, period.VigenteDesde);
        Assert.Equal(EstadoHabito.Activo, period.Estado);
        Assert.Equal(habit.Id, period.HabitoId);
        Assert.Equal("billete", result.IconoId);
        Assert.Equal(CatalogoCategoriasHabito.Todas[4].Id, result.CategoriaId);
        Assert.Equal(1, repository.CantidadDeInserciones);
        Assert.Equal(cancellation.Token, categories.Token);
        Assert.Equal(1, categories.Consultas);
    }

    [Theory]
    [InlineData("empty-category")]
    [InlineData("unknown-category")]
    [InlineData("missing-category")]
    [InlineData("empty-icon")]
    [InlineData("null-icon")]
    [InlineData("space-icon")]
    [InlineData("unknown-icon")]
    [InlineData("case-icon")]
    [InlineData("name")]
    [InlineData("null-name")]
    [InlineData("days")]
    [InlineData("null-days")]
    [InlineData("enum")]
    [InlineData("zone")]
    [InlineData("snapshot")]
    public async Task Creacion_Invalida_NoPersisteNiConsultaRelojAntesDeValidar(string scenario)
    {
        var user = new Usuario("Test", "date@example.invalid", "hash",
            scenario == "zone" ? "Invalid/Zone" : "Etc/UTC");
        var repository = new FakeHabitoRepository();
        var clock = new Reloj();
        var categories = new Categorias(scenario != "missing-category");
        var useCase = new CrearHabito(new FakeUsuarioRepository(user), repository,
            new EstadoHabitoReadinessRepositoryFalso(snapshotCapturado: scenario != "snapshot"),
            categories, new FechaLocalUsuario(clock, new ZonaHorariaIana()), clock);
        var request = CrearSolicitud(user.Id);
        request = scenario switch
        {
            "empty-category" => request with { CategoriaId = Guid.Empty },
            "unknown-category" => request with { CategoriaId = Guid.Parse("11111111-1111-1111-1111-111111111111") },
            "empty-icon" => request with { IconoId = "" },
            "null-icon" => request with { IconoId = null! },
            "space-icon" => request with { IconoId = " " },
            "unknown-icon" => request with { IconoId = "unknown" },
            "case-icon" => request with { IconoId = "Libro" },
            "name" => request with { Nombre = " " },
            "null-name" => request with { Nombre = null! },
            "days" => request with { DiasProgramados = [] },
            "null-days" => request with { DiasProgramados = null! },
            "enum" => request with { DiasProgramados = [(DayOfWeek)7] },
            _ => request
        };
        if (scenario == "zone")
            await Assert.ThrowsAsync<ZonaHorariaPersistidaInvalidaException>(() => useCase.EjecutarAsync(request));
        else if (scenario == "snapshot")
            await Assert.ThrowsAsync<HistorialEstadoHabitoNoInicializadoException>(() => useCase.EjecutarAsync(request));
        else if (scenario is "name" or "days" or "enum")
            await Assert.ThrowsAsync<ArgumentException>(() => useCase.EjecutarAsync(request));
        else if (scenario is "null-name" or "null-days")
            await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.EjecutarAsync(request));
        else
        {
            var error = await Assert.ThrowsAsync<MetadatosHabitoInvalidosException>(() => useCase.EjecutarAsync(request));
            Assert.Equal(scenario.Contains("category") ? "categoriaId" : "iconoId", error.Campo);
        }
        Assert.Null(repository.HabitoPersistido);
        Assert.Equal(0, repository.CantidadDeInserciones);
        Assert.Equal(scenario == "zone" ? 1 : 0, clock.Lecturas);
    }

    [Fact]
    public void ConversionPura_NoLeeReloj_YObtenerHoyConservaUnaLectura()
    {
        var clock = new Reloj();
        var service = new FechaLocalUsuario(clock, new ZonaHorariaIana());
        Assert.Equal(new DateOnly(2026, 10, 9),
            service.ObtenerFechaLocal(clock.Instante, "America/Argentina/Buenos_Aires"));
        Assert.Equal(0, clock.Lecturas);
        Assert.Equal(new DateOnly(2026, 10, 10), service.ObtenerHoy("Etc/UTC"));
        Assert.Equal(1, clock.Lecturas);
    }

    [Theory]
    [InlineData("category")]
    [InlineData("icon")]
    [InlineData("offset")]
    public void Domain_AltaNoPermiteOmitirInvariantes(string invalid)
    {
        var timestamp = DateTimeOffset.Parse(invalid == "offset" ? "2026-10-10T12:00:00+03:00" : "2026-10-10T12:00:00Z");
        Assert.Throws<ArgumentException>(() => new Habito(CrearUsuario(), "Leer", null,
            timestamp, new DateOnly(2026, 10, 10),
            invalid == "category" ? Guid.Empty : CatalogoCategoriasHabito.Todas[4].Id,
            invalid == "icon" ? "Libro" : "libro", [DayOfWeek.Monday]));
    }

    [Fact]
    public void Historico_SinClasificacion_ConservaFechasYNoAbreConstructorPublicoAlternativo()
    {
        var historic = HabitoHistorico.Crear(CrearUsuario(), "Histórico", null,
            new DateOnly(2020, 1, 1), [DayOfWeek.Monday]);
        Assert.Null(historic.CategoriaId);
        Assert.Null(historic.IconoId);
        Assert.Equal(new DateOnly(2020, 1, 1), historic.FechaInicio);
        var constructor = Assert.Single(typeof(Habito).GetConstructors());
        Assert.Contains(constructor.GetParameters(), p => p.Name == "categoriaId" && p.ParameterType == typeof(Guid));
        Assert.Contains(constructor.GetParameters(), p => p.Name == "iconoId" && p.ParameterType == typeof(string));
    }
}
