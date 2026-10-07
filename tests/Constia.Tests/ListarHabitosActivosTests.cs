using Constia.Application.Habitos;
using Constia.Domain;

namespace Constia.Tests;

public sealed class ListarHabitosActivosTests
{
    [Fact]
    public async Task EjecutarAsync_UsaUsuarioIdYDevuelveLosResultadosDelRepositorio()
    {
        var usuario = CrearUsuario();
        var habitos = new[]
        {
            CrearHabito(usuario, "Leer", new DateOnly(2026, 10, 6), [DayOfWeek.Monday]),
            CrearHabito(usuario, "Caminar", new DateOnly(2026, 10, 7), [DayOfWeek.Wednesday])
        };
        var repositorio = new FakeHabitoRepository(habitos);
        var casoDeUso = new ListarHabitosActivos(repositorio);
        using var cancellation = new CancellationTokenSource();

        var resultado = await casoDeUso.EjecutarAsync(usuario.Id, cancellation.Token);

        Assert.Equal(usuario.Id, repositorio.UsuarioIdRecibido);
        Assert.Equal(cancellation.Token, repositorio.CancellationTokenRecibido);
        Assert.Equal(habitos.Select(habito => habito.Id), resultado.Select(habito => habito.Id));
        Assert.Equal("Leer", resultado[0].Nombre);
        Assert.Equal([DayOfWeek.Monday], resultado[0].DiasProgramados);
        Assert.Equal("Caminar", resultado[1].Nombre);
    }

    [Fact]
    public async Task EjecutarAsync_SinResultados_DevuelveColeccionVacia()
    {
        var casoDeUso = new ListarHabitosActivos(new FakeHabitoRepository([]));

        var resultado = await casoDeUso.EjecutarAsync(Guid.NewGuid());

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task EjecutarAsync_NoFiltraPorFechaNiDiaProgramado()
    {
        var habit = CrearHabito(
            CrearUsuario(),
            "Hábito futuro",
            new DateOnly(2099, 12, 31),
            [DayOfWeek.Sunday]);
        var casoDeUso = new ListarHabitosActivos(new FakeHabitoRepository([habit]));

        var resultado = await casoDeUso.EjecutarAsync(habit.Usuario.Id);

        Assert.Single(resultado);
        Assert.Equal(new DateOnly(2099, 12, 31), resultado[0].FechaInicio);
        Assert.Equal([DayOfWeek.Sunday], resultado[0].DiasProgramados);
    }

    private static Usuario CrearUsuario() => new("Ana", $"{Guid.NewGuid()}@example.com", "hashed-value");

    private static Habito CrearHabito(
        Usuario usuario,
        string nombre,
        DateOnly fechaInicio,
        IReadOnlyCollection<DayOfWeek> dias) => new(usuario, nombre, null, fechaInicio, dias);

    private sealed class FakeHabitoRepository(IReadOnlyList<Habito> resultados) : IHabitoRepository
    {
        public Guid? UsuarioIdRecibido { get; private set; }

        public CancellationToken CancellationTokenRecibido { get; private set; }

        public Task AgregarAsync(Habito habito, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<Habito>> ListarActivosPorUsuarioAsync(
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
