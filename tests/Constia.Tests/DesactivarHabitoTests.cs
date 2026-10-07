using Constia.Application.Habitos;
using Constia.Domain;

namespace Constia.Tests;

public sealed class DesactivarHabitoTests
{
    [Fact]
    public async Task EjecutarAsync_ConHabitoPropio_DesactivaPersisteYPropagaParametros()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hashed-value");
        var habit = new Habito(usuario, "Leer", "Un capítulo", new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);
        var repository = new FakeHabitoRepository(habit);
        var useCase = new DesactivarHabito(repository);
        using var cancellation = new CancellationTokenSource();

        var result = await useCase.EjecutarAsync(habit.Id, usuario.Id, cancellation.Token);

        Assert.True(result);
        Assert.Equal(habit.Id, repository.HabitIdRecibido);
        Assert.Equal(usuario.Id, repository.UsuarioIdRecibido);
        Assert.Equal(cancellation.Token, repository.CancellationTokenBusqueda);
        Assert.Equal(cancellation.Token, repository.CancellationTokenGuardado);
        Assert.Equal(1, repository.CantidadDeGuardados);
        Assert.Equal(EstadoHabito.Inactivo, habit.Estado);
        Assert.Contains(habit, repository.Habitos);
        Assert.Equal("Leer", habit.Nombre);
        Assert.Equal("Un capítulo", habit.Descripcion);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], habit.DiasProgramados.Select(dia => dia.Dia));
    }

    [Fact]
    public async Task EjecutarAsync_ConHabitoYaInactivo_DevuelveTrueYPersiste()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hashed-value");
        var habit = new Habito(usuario, "Leer", null, new DateOnly(2026, 10, 5), [DayOfWeek.Monday]);
        habit.Desactivar();
        var repository = new FakeHabitoRepository(habit);
        var useCase = new DesactivarHabito(repository);

        var result = await useCase.EjecutarAsync(habit.Id, usuario.Id);

        Assert.True(result);
        Assert.Equal(EstadoHabito.Inactivo, habit.Estado);
        Assert.Equal(1, repository.CantidadDeGuardados);
    }

    [Fact]
    public async Task EjecutarAsync_SinCoincidencia_DevuelveFalseYSinGuardar()
    {
        var habit = new Habito(
            new Usuario("Ana", "ana@example.com", "hashed-value"),
            "Leer",
            null,
            new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday]);
        var repository = new FakeHabitoRepository(habit);
        var useCase = new DesactivarHabito(repository);

        var result = await useCase.EjecutarAsync(habit.Id, Guid.NewGuid());

        Assert.False(result);
        Assert.Equal(EstadoHabito.Activo, habit.Estado);
        Assert.Equal(0, repository.CantidadDeGuardados);
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

        public Task GuardarCambiosAsync(CancellationToken cancellationToken)
        {
            CantidadDeGuardados++;
            CancellationTokenGuardado = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
