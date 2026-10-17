using Constia.Application.Habitos;
using Constia.Application.Temporal;
using Constia.Domain;

namespace Constia.Tests;

public sealed class DesactivarHabitoTests
{
    [Fact]
    public async Task EjecutarAsync_ConHabitoPropio_DesactivaPersisteYPropagaParametros()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hashed-value", "Etc/UTC");
        var habit = new Habito(usuario, "Leer", "Un capítulo", new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);
        var repository = new FakeHabitoRepository(habit);
        var fechaLocal = new FechaLocalFija(new DateOnly(2026, 10, 10));
        var useCase = new DesactivarHabito(repository, fechaLocal, new EstadoHabitoReadinessRepositoryFalso());
        using var cancellation = new CancellationTokenSource();

        var result = await useCase.EjecutarAsync(habit.Id, usuario.Id, cancellation.Token);

        Assert.True(result);
        Assert.Equal(habit.Id, repository.HabitIdRecibido);
        Assert.Equal(usuario.Id, repository.UsuarioIdRecibido);
        Assert.Equal(cancellation.Token, repository.CancellationTokenBusqueda);
        Assert.Equal(cancellation.Token, repository.CancellationTokenGuardado);
        Assert.Equal(1, repository.CantidadDeGuardados);
        Assert.Equal(EstadoHabito.Inactivo, habit.Estado);
        Assert.Equal("Etc/UTC", fechaLocal.TimeZoneIdRecibido);
        Assert.Equal(EstadoHabito.Inactivo, habit.ObtenerEstadoPara(new DateOnly(2026, 10, 10)));
        Assert.Equal(new DateOnly(2026, 10, 10), habit.PeriodosEstado.Single(p => p.VigenteHasta is null).VigenteDesde);
        Assert.Contains(habit, repository.Habitos);
        Assert.Equal("Leer", habit.Nombre);
        Assert.Equal("Un capítulo", habit.Descripcion);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], habit.DiasProgramados.Select(dia => dia.Dia));
    }

    [Fact]
    public async Task EjecutarAsync_ConHabitoYaInactivo_DevuelveTrueYSinIncrementarToken()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hashed-value", "Etc/UTC");
        var habit = new Habito(usuario, "Leer", null, new DateOnly(2026, 10, 5), [DayOfWeek.Monday]);
        habit.Desactivar(habit.FechaInicio);
        var repository = new FakeHabitoRepository(habit);
        var useCase = CrearCaso(repository);

        var result = await useCase.EjecutarAsync(habit.Id, usuario.Id);

        Assert.True(result);
        Assert.Equal(EstadoHabito.Inactivo, habit.Estado);
        Assert.Equal(0, repository.CantidadDeGuardados);
    }

    [Fact]
    public async Task EjecutarAsync_SinCoincidencia_DevuelveFalseYSinGuardar()
    {
        var habit = new Habito(
            new Usuario("Ana", "ana@example.com", "hashed-value", "Etc/UTC"),
            "Leer",
            null,
            new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday]);
        var repository = new FakeHabitoRepository(habit);
        var useCase = CrearCaso(repository);

        var result = await useCase.EjecutarAsync(habit.Id, Guid.NewGuid());

        Assert.False(result);
        Assert.Equal(EstadoHabito.Activo, habit.Estado);
        Assert.Equal(0, repository.CantidadDeGuardados);
    }

    [Fact]
    public async Task EjecutarAsync_HistorialNoCompleto_BloqueaLaDesactivacion()
    {
        var habit = new Habito(new Usuario("Ana", "ana@example.com", "hashed-value", "Etc/UTC"),
            "Leer", null, new DateOnly(2026, 10, 5), [DayOfWeek.Monday]);
        var repository = new FakeHabitoRepository(habit);
        var useCase = new DesactivarHabito(repository, new FechaLocalFija(new DateOnly(2026, 10, 10)),
            new EstadoHabitoReadinessRepositoryFalso(historialCompleto: false));

        await Assert.ThrowsAsync<HistorialEstadoHabitoNoInicializadoException>(() =>
            useCase.EjecutarAsync(habit.Id, habit.Usuario.Id));

        Assert.Equal(EstadoHabito.Activo, habit.Estado);
        Assert.Equal(0, repository.CantidadDeGuardados);
    }

    private static DesactivarHabito CrearCaso(FakeHabitoRepository repository) => new(
        repository,
        new FechaLocalFija(new DateOnly(2026, 10, 10)),
        new EstadoHabitoReadinessRepositoryFalso());

    private sealed class FechaLocalFija(DateOnly hoy) : IFechaLocalUsuario
    {
        public string? TimeZoneIdRecibido { get; private set; }

        public DateOnly ObtenerHoy(string timeZoneId)
        {
            TimeZoneIdRecibido = timeZoneId;
            return hoy;
        }
    }

    private sealed class FakeHabitoRepository(Habito habit) : IHabitoRepository
    {
        public Guid? HabitIdRecibido { get; private set; }

        public Guid? UsuarioIdRecibido { get; private set; }

        public CancellationToken CancellationTokenBusqueda { get; private set; }

        public CancellationToken CancellationTokenGuardado { get; private set; }

        public int CantidadDeGuardados { get; private set; }

        public IReadOnlyCollection<Habito> Habitos => [habit];

        public Task AgregarAsync(Habito habito, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Habito>> ListarActivosPorUsuarioAsync(
            Guid usuarioId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Habito>> ListarInactivosPorUsuarioAsync(
            Guid usuarioId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Habito?> BuscarPorIdYUsuarioAsync(
            Guid habitId,
            Guid usuarioId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Habito?> BuscarParaModificarPorIdYUsuarioAsync(
            Guid habitId,
            Guid usuarioId,
            CancellationToken cancellationToken)
        {
            HabitIdRecibido = habitId;
            UsuarioIdRecibido = usuarioId;
            CancellationTokenBusqueda = cancellationToken;
            return Task.FromResult<Habito?>(
                habit.Id == habitId && habit.Usuario.Id == usuarioId ? habit : null);
        }

        public Task GuardarCambiosAsync(Habito habito, CancellationToken cancellationToken)
        {
            Assert.Same(habit, habito);
            CantidadDeGuardados++;
            CancellationTokenGuardado = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
