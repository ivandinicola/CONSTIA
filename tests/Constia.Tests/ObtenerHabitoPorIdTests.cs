using Constia.Application.Habitos;
using Constia.Domain;

namespace Constia.Tests;

public sealed class ObtenerHabitoPorIdTests
{
    [Fact]
    public async Task EjecutarAsync_ConCoincidencia_PropagaParametrosYMapeaHabitoYDias()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hashed-value");
        var habitId = Guid.NewGuid();
        var habit = new Habito(
            usuario,
            "Leer",
            "Un capítulo",
            new DateOnly(2026, 10, 6),
            [DayOfWeek.Monday, DayOfWeek.Friday]);
        typeof(Habito).GetProperty(nameof(Habito.Id))!.SetValue(habit, habitId);
        var repository = new FakeHabitoRepository(habit);
        var useCase = new ObtenerHabitoPorId(repository);
        var usuarioId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();

        var result = await useCase.EjecutarAsync(habitId, usuarioId, cancellation.Token);

        Assert.Equal(habitId, repository.HabitIdRecibido);
        Assert.Equal(usuarioId, repository.UsuarioIdRecibido);
        Assert.Equal(cancellation.Token, repository.CancellationTokenRecibido);
        Assert.NotNull(result);
        Assert.Equal(habit.Id, result.Id);
        Assert.Equal(habit.Nombre, result.Nombre);
        Assert.Equal(habit.Descripcion, result.Descripcion);
        Assert.Equal(habit.FechaCreacion, result.FechaCreacion);
        Assert.Equal(habit.FechaInicio, result.FechaInicio);
        Assert.Equal(habit.Estado, result.Estado);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], result.DiasProgramados);
    }

    [Fact]
    public async Task EjecutarAsync_SinCoincidencia_DevuelveNull()
    {
        var useCase = new ObtenerHabitoPorId(new FakeHabitoRepository(null));

        var result = await useCase.EjecutarAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    private sealed class FakeHabitoRepository(Habito? resultado) : IHabitoRepository
    {
        public Guid? HabitIdRecibido { get; private set; }

        public Guid? UsuarioIdRecibido { get; private set; }

        public CancellationToken CancellationTokenRecibido { get; private set; }

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
            CancellationToken cancellationToken)
        {
            HabitIdRecibido = habitId;
            UsuarioIdRecibido = usuarioId;
            CancellationTokenRecibido = cancellationToken;
            return Task.FromResult<Habito?>(resultado);
        }

        public Task<Habito?> BuscarParaModificarPorIdYUsuarioAsync(
            Guid habitId,
            Guid usuarioId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task GuardarCambiosAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
