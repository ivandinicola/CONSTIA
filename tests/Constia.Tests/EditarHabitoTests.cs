using Constia.Application.Habitos;
using Constia.Domain;

namespace Constia.Tests;

public sealed class EditarHabitoTests
{
    [Fact]
    public async Task EjecutarAsync_ConCoincidencia_ActualizaPersisteYDevuelveElResultado()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hashed-value");
        var habit = new Habito(usuario, "Leer", "Antes", new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);
        var originalCreatedAt = habit.FechaCreacion;
        var originalStartDate = habit.FechaInicio;
        var repository = new FakeHabitoRepository(habit);
        var useCase = new EditarHabito(repository);
        var request = new EditarHabitoRequest("Caminar", "Después", [DayOfWeek.Wednesday, DayOfWeek.Friday]);
        var usuarioId = usuario.Id;
        using var cancellation = new CancellationTokenSource();

        var result = await useCase.EjecutarAsync(habit.Id, usuarioId, request, cancellation.Token);

        Assert.Equal(habit.Id, repository.HabitIdRecibido);
        Assert.Equal(usuarioId, repository.UsuarioIdRecibido);
        Assert.Equal(cancellation.Token, repository.CancellationTokenBusqueda);
        Assert.Equal(cancellation.Token, repository.CancellationTokenGuardado);
        Assert.Equal(1, repository.CantidadDeGuardados);
        Assert.Equal("Caminar", habit.Nombre);
        Assert.Equal("Después", habit.Descripcion);
        Assert.Equal([DayOfWeek.Wednesday, DayOfWeek.Friday], habit.DiasProgramados.Select(dia => dia.Dia));
        Assert.NotNull(result);
        Assert.Equal(habit.Id, result.Id);
        Assert.Equal("Caminar", result.Nombre);
        Assert.Equal("Después", result.Descripcion);
        Assert.Equal(originalCreatedAt, result.FechaCreacion);
        Assert.Equal(originalStartDate, result.FechaInicio);
        Assert.Equal([DayOfWeek.Wednesday, DayOfWeek.Friday], result.DiasProgramados);
    }

    [Fact]
    public async Task EjecutarAsync_SinCoincidencia_DevuelveNullYSinGuardar()
    {
        var repository = new FakeHabitoRepository(null);
        var useCase = new EditarHabito(repository);

        var result = await useCase.EjecutarAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new EditarHabitoRequest("Caminar", null, [DayOfWeek.Friday]));

        Assert.Null(result);
        Assert.Equal(0, repository.CantidadDeGuardados);
    }

    private sealed class FakeHabitoRepository(Habito? habit) : IHabitoRepository
    {
        public Guid? HabitIdRecibido { get; private set; }

        public Guid? UsuarioIdRecibido { get; private set; }

        public CancellationToken CancellationTokenBusqueda { get; private set; }

        public CancellationToken CancellationTokenGuardado { get; private set; }

        public int CantidadDeGuardados { get; private set; }

        public Task AgregarAsync(Habito habito, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Habito>> ListarActivosPorUsuarioAsync(
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
                habit is not null && habit.Id == habitId && habit.Usuario.Id == usuarioId ? habit : null);
        }

        public Task GuardarCambiosAsync(CancellationToken cancellationToken)
        {
            CantidadDeGuardados++;
            CancellationTokenGuardado = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
