using Constia.Application.Habitos;
using Constia.Domain;

namespace Constia.Tests;

public sealed class ListarHabitosInactivosTests
{
    [Fact]
    public async Task EjecutarAsync_PropagaUsuarioYCancellationTokenYMapeaHabitosYDias()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hashed-value");
        var habit = new Habito(
            usuario,
            "Hábito archivado",
            "Descripción conservada",
            new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday, DayOfWeek.Friday]);
        habit.Desactivar();
        var repository = new FakeHabitoRepository([habit]);
        var useCase = new ListarHabitosInactivos(repository);
        using var cancellation = new CancellationTokenSource();

        var result = await useCase.EjecutarAsync(usuario.Id, cancellation.Token);

        Assert.Equal(usuario.Id, repository.UsuarioIdRecibido);
        Assert.Equal(cancellation.Token, repository.CancellationTokenRecibido);
        var listed = Assert.Single(result);
        Assert.Equal(habit.Id, listed.Id);
        Assert.Equal(habit.Nombre, listed.Nombre);
        Assert.Equal(habit.Descripcion, listed.Descripcion);
        Assert.Equal(EstadoHabito.Inactivo, listed.Estado);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], listed.DiasProgramados);
    }

    [Fact]
    public async Task EjecutarAsync_SinResultados_DevuelveColeccionVacia()
    {
        var useCase = new ListarHabitosInactivos(new FakeHabitoRepository([]));

        var result = await useCase.EjecutarAsync(Guid.NewGuid());

        Assert.Empty(result);
    }

    private sealed class FakeHabitoRepository(IReadOnlyList<Habito> resultados) : IHabitoRepository
    {
        public Guid? UsuarioIdRecibido { get; private set; }

        public CancellationToken CancellationTokenRecibido { get; private set; }

        public Task AgregarAsync(Habito habito, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Habito>> ListarActivosPorUsuarioAsync(
            Guid usuarioId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Habito>> ListarInactivosPorUsuarioAsync(
            Guid usuarioId,
            CancellationToken cancellationToken)
        {
            UsuarioIdRecibido = usuarioId;
            CancellationTokenRecibido = cancellationToken;
            return Task.FromResult(resultados);
        }

        public Task<Habito?> BuscarPorIdYUsuarioAsync(
            Guid habitId,
            Guid usuarioId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Habito?> BuscarParaModificarPorIdYUsuarioAsync(
            Guid habitId,
            Guid usuarioId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task GuardarCambiosAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
