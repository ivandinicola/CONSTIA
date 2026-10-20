using Constia.Application.Cumplimientos;
using Constia.Application.Habitos;
using Constia.Application.Temporal;
using Constia.Application.Usuarios;
using Constia.Domain;
using System.Reflection;

namespace Constia.Tests;

public sealed class RegistrarCumplimientoTests
{
    private static readonly DateTimeOffset InstanteUtc = new(2026, 10, 10, 2, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly HoyBuenosAires = new(2026, 10, 9);

    [Fact]
    public async Task EjecutarAsync_RegistraHoySegunZonaHorariaDelUsuario()
    {
        var usuario = CrearUsuario("America/Argentina/Buenos_Aires");
        var habit = CrearHabito(usuario, [DayOfWeek.Friday]);
        var store = CrearCaso(usuario, habit);
        var requestDate = HoyBuenosAires;

        var result = await store.Caso.EjecutarAsync(habit.Id, usuario.Id, requestDate);

        Assert.NotNull(result);
        Assert.Equal(requestDate, result.Fecha);
        Assert.Equal(usuario.TimeZoneId, store.FechaLocal.TimeZoneIdRecibido);
        var persisted = Assert.Single(store.Cumplimientos.Agregados);
        Assert.Same(habit, persisted.Habito);
        Assert.Equal(requestDate, persisted.Fecha);
    }

    [Fact]
    public async Task EjecutarAsync_RegistraAyerYUsaLaProgramacionHistoricaAplicable()
    {
        var usuario = CrearUsuario("America/Argentina/Buenos_Aires");
        var habit = CrearHabito(usuario, [DayOfWeek.Monday]);
        habit.ActualizarConfiguracionVersionada(
            habit.Nombre,
            habit.Descripcion,
            [DayOfWeek.Thursday],
            new DateOnly(2026, 10, 8));
        var store = CrearCaso(usuario, habit);

        var result = await store.Caso.EjecutarAsync(habit.Id, usuario.Id, new DateOnly(2026, 10, 8));

        Assert.NotNull(result);
        Assert.Single(store.Cumplimientos.Agregados);
    }

    [Theory]
    [InlineData(2026, 10, 7)]
    [InlineData(2026, 10, 10)]
    public async Task EjecutarAsync_AAnteayerOFuturo_LanzaArgumentException(
        int year,
        int month,
        int day)
    {
        var usuario = CrearUsuario("America/Argentina/Buenos_Aires");
        var habit = CrearHabito(usuario, [DayOfWeek.Wednesday, DayOfWeek.Saturday]);
        var store = CrearCaso(usuario, habit);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.Caso.EjecutarAsync(habit.Id, usuario.Id, new DateOnly(year, month, day)));

        Assert.Empty(store.Cumplimientos.Agregados);
    }

    [Fact]
    public async Task EjecutarAsync_AntesDeFechaInicio_LanzaArgumentException()
    {
        var usuario = CrearUsuario("America/Argentina/Buenos_Aires");
        var habit = HabitoHistorico.Crear(usuario, "Leer", null, HoyBuenosAires, [DayOfWeek.Friday]);
        var store = CrearCaso(usuario, habit);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.Caso.EjecutarAsync(habit.Id, usuario.Id, HoyBuenosAires.AddDays(-1)));

        Assert.Empty(store.Cumplimientos.Agregados);
    }

    [Fact]
    public async Task EjecutarAsync_DiaNoProgramado_LanzaArgumentException()
    {
        var usuario = CrearUsuario("America/Argentina/Buenos_Aires");
        var habit = CrearHabito(usuario, [DayOfWeek.Monday]);
        var store = CrearCaso(usuario, habit);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.Caso.EjecutarAsync(habit.Id, usuario.Id, HoyBuenosAires));

        Assert.Empty(store.Cumplimientos.Agregados);
    }

    [Fact]
    public async Task EjecutarAsync_HabitoInactivo_LanzaConflictoYSinPersistir()
    {
        var usuario = CrearUsuario("Etc/UTC");
        var habit = CrearHabito(usuario, [DayOfWeek.Saturday]);
        habit.Desactivar(habit.FechaInicio);
        var store = CrearCaso(usuario, habit);

        await Assert.ThrowsAsync<ConflictoRegistroCumplimientoException>(() =>
            store.Caso.EjecutarAsync(habit.Id, usuario.Id, new DateOnly(2026, 10, 10)));

        Assert.Empty(store.Cumplimientos.Agregados);
    }

    [Fact]
    public async Task EjecutarAsync_Duplicado_LanzaConflictoYSinPersistir()
    {
        var usuario = CrearUsuario("Etc/UTC");
        var habit = CrearHabito(usuario, [DayOfWeek.Saturday]);
        var store = CrearCaso(usuario, habit, duplicado: true);

        await Assert.ThrowsAsync<ConflictoRegistroCumplimientoException>(() =>
            store.Caso.EjecutarAsync(habit.Id, usuario.Id, new DateOnly(2026, 10, 10)));

        Assert.Empty(store.Cumplimientos.Agregados);
    }

    [Fact]
    public async Task EjecutarAsync_HabitoAjenoOInexistente_DevuelveNull()
    {
        var usuario = CrearUsuario("Etc/UTC");
        var habit = CrearHabito(CrearUsuario("Etc/UTC"), [DayOfWeek.Saturday]);
        var store = CrearCaso(usuario, habit);

        var result = await store.Caso.EjecutarAsync(habit.Id, usuario.Id, new DateOnly(2026, 10, 10));

        Assert.Null(result);
        Assert.Empty(store.Cumplimientos.Agregados);
    }

    [Fact]
    public async Task EjecutarAsync_UsuarioAutenticadoYaNoExiste_LanzaExcepcionSinPersistir()
    {
        var usuario = CrearUsuario("Etc/UTC");
        var habit = CrearHabito(usuario, [DayOfWeek.Saturday]);
        var store = CrearCaso(null, habit);

        await Assert.ThrowsAsync<UsuarioAutenticadoNoEncontradoException>(() =>
            store.Caso.EjecutarAsync(habit.Id, usuario.Id, new DateOnly(2026, 10, 10)));

        Assert.Empty(store.Cumplimientos.Agregados);
    }

    [Fact]
    public async Task EjecutarAsync_ZonaPersistidaInvalida_NoUsaFallback()
    {
        var usuario = CrearUsuario("Invalid/Unknown");
        var habit = CrearHabito(usuario, [DayOfWeek.Saturday]);
        var fechaLocal = new FechaLocalUsuario(
            new RelojFijo(InstanteUtc),
            new ZonaHorariaIana());
        var caso = new RegistrarCumplimiento(
            new FakeUsuarioRepository(usuario),
            new FakeHabitoRepository(habit),
            new FakeCumplimientoRepository(),
            fechaLocal,
            new EstadoHabitoReadinessRepositoryFalso());

        await Assert.ThrowsAsync<ZonaHorariaPersistidaInvalidaException>(() =>
            caso.EjecutarAsync(habit.Id, usuario.Id, new DateOnly(2026, 10, 10)));
    }

    private static Usuario CrearUsuario(string timeZoneId) =>
        new("Ana", $"{Guid.NewGuid():N}@example.test", "synthetic-hash", timeZoneId);

    private static Habito CrearHabito(Usuario usuario, DayOfWeek[] days) =>
        HabitoHistorico.Crear(usuario, "Leer", null, new DateOnly(2026, 1, 1), days);

    private static (RegistrarCumplimiento Caso, FakeFechaLocalUsuario FechaLocal, FakeCumplimientoRepository Cumplimientos)
        CrearCaso(Usuario? usuario, Habito habit, bool duplicado = false)
    {
        var hoy = usuario?.TimeZoneId == "Etc/UTC" ? new DateOnly(2026, 10, 10) : HoyBuenosAires;
        var fechaLocal = new FakeFechaLocalUsuario(hoy);
        var cumplimientos = new FakeCumplimientoRepository(duplicado);
        var caso = new RegistrarCumplimiento(
            new FakeUsuarioRepository(usuario),
            new FakeHabitoRepository(habit),
            cumplimientos,
            fechaLocal,
            new EstadoHabitoReadinessRepositoryFalso());
        return (caso, fechaLocal, cumplimientos);
    }

    [Fact]
    public async Task EjecutarAsync_HistorialNoCompleto_BloqueaAntesDeGuardar()
    {
        var usuario = CrearUsuario("Etc/UTC");
        var habit = CrearHabito(usuario, [DayOfWeek.Saturday]);
        var completions = new FakeCumplimientoRepository();
        var useCase = new RegistrarCumplimiento(
            new FakeUsuarioRepository(usuario),
            new FakeHabitoRepository(habit),
            completions,
            new FakeFechaLocalUsuario(new DateOnly(2026, 10, 10)),
            new EstadoHabitoReadinessRepositoryFalso(historialCompleto: false));

        await Assert.ThrowsAsync<HistorialEstadoHabitoNoInicializadoException>(() =>
            useCase.EjecutarAsync(habit.Id, usuario.Id, new DateOnly(2026, 10, 10)));

        Assert.Empty(completions.Agregados);
    }

    [Fact]
    public async Task EjecutarAsync_HistorialDelDiaDesconocido_NoLoTrataComoInactivoNiPersiste()
    {
        var usuario = CrearUsuario("Etc/UTC");
        var habit = CrearHabito(usuario, [DayOfWeek.Saturday]);
        var periods = (List<PeriodoEstadoHabito>)typeof(Habito)
            .GetField("_periodosEstado", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(habit)!;
        periods.Clear();
        var store = CrearCaso(usuario, habit);

        await Assert.ThrowsAsync<EstadoHabitoHistoricoDesconocidoException>(() =>
            store.Caso.EjecutarAsync(habit.Id, usuario.Id, new DateOnly(2026, 10, 10)));

        Assert.Equal(EstadoHabito.Activo, habit.Estado);
        Assert.Empty(store.Cumplimientos.Agregados);
    }

    private sealed class FakeFechaLocalUsuario(DateOnly hoy) : IFechaLocalUsuario
    {
        public DateOnly ObtenerFechaLocal(DateTimeOffset instanteUtc, string timeZoneId) =>
            throw new NotSupportedException("Este fake solo prueba consumidores de ObtenerHoy.");

        public string? TimeZoneIdRecibido { get; private set; }

        public DateOnly ObtenerHoy(string timeZoneId)
        {
            TimeZoneIdRecibido = timeZoneId;
            return hoy;
        }
    }

    private sealed class FakeUsuarioRepository(Usuario? persisted) : IUsuarioRepository
    {
        public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(persisted?.Id == id ? persisted : null);

        public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult<Usuario?>(null);

        public Task<bool> ExistePorEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> IntentarAgregarAsync(Usuario usuario, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task GuardarCambiosAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeHabitoRepository(Habito habit) : IHabitoRepository
    {
        public Task<Habito?> BuscarParaModificarPorIdYUsuarioAsync(
            Guid habitId,
            Guid usuarioId,
            CancellationToken cancellationToken) =>
            Task.FromResult(habit.Id == habitId && habit.Usuario.Id == usuarioId ? habit : null);

        public Task AgregarAsync(Habito habito, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Habito>> ListarActivosPorUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Habito>> ListarInactivosPorUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Habito?> BuscarPorIdYUsuarioAsync(Guid habitId, Guid usuarioId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task GuardarCambiosAsync(Habito habito, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeCumplimientoRepository(bool duplicate = false) : ICumplimientoRepository
    {
        public List<Cumplimiento> Agregados { get; } = [];

        public Task<bool> ExistePorHabitoYFechaAsync(
            Guid habitId,
            DateOnly fecha,
            CancellationToken cancellationToken) =>
            Task.FromResult(duplicate || Agregados.Any(item => item.Habito.Id == habitId && item.Fecha == fecha));

        public Task AgregarAsync(Cumplimiento cumplimiento, CancellationToken cancellationToken)
        {
            Agregados.Add(cumplimiento);
            return Task.CompletedTask;
        }
    }

    private sealed class RelojFijo(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
