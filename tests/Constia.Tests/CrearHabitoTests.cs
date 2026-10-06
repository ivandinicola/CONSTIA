using Constia.Application.Habitos;
using Constia.Application.Usuarios;
using Constia.Domain;

namespace Constia.Tests;

public class CrearHabitoTests
{
    [Fact]
    public async Task EjecutarAsync_ConDatosValidos_UsaElPropietarioYPersisteHabito()
    {
        var usuario = CrearUsuario();
        var usuarios = new FakeUsuarioRepository(usuario);
        var habitos = new FakeHabitoRepository();
        var casoDeUso = new CrearHabito(usuarios, habitos);
        using var cancellation = new CancellationTokenSource();

        var resultado = await casoDeUso.EjecutarAsync(
            CrearSolicitud(usuario.Id),
            cancellation.Token);

        var habitoPersistido = Assert.IsType<Habito>(habitos.HabitoPersistido);
        Assert.Equal(usuario.Id, usuarios.IdBuscado);
        Assert.Same(usuario, habitoPersistido.Usuario);
        Assert.Same(habitoPersistido, habitos.HabitoPersistido);
        Assert.Equal(cancellation.Token, usuarios.CancellationTokenRecibido);
        Assert.Equal(cancellation.Token, habitos.CancellationTokenRecibido);
        Assert.Equal(1, habitos.CantidadDeInserciones);
        Assert.IsType<HabitoCreado>(resultado);
    }

    [Fact]
    public async Task EjecutarAsync_ConNombreInvalido_DelegaValidacionAlDomainYSinPersistir()
    {
        var usuario = CrearUsuario();
        var habitos = new FakeHabitoRepository();
        var casoDeUso = new CrearHabito(
            new FakeUsuarioRepository(usuario),
            habitos);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            casoDeUso.EjecutarAsync(CrearSolicitud(usuario.Id, nombre: " ")));

        Assert.Null(habitos.HabitoPersistido);
        Assert.Equal(0, habitos.CantidadDeInserciones);
    }

    [Fact]
    public async Task EjecutarAsync_SinDiasProgramados_DelegaValidacionAlDomainYSinPersistir()
    {
        var usuario = CrearUsuario();
        var habitos = new FakeHabitoRepository();
        var casoDeUso = new CrearHabito(
            new FakeUsuarioRepository(usuario),
            habitos);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            casoDeUso.EjecutarAsync(CrearSolicitud(usuario.Id, diasProgramados: [])));

        Assert.Null(habitos.HabitoPersistido);
        Assert.Equal(0, habitos.CantidadDeInserciones);
    }

    [Fact]
    public async Task EjecutarAsync_CuandoUsuarioNoExiste_DevuelveNullYSinPersistir()
    {
        var habitos = new FakeHabitoRepository();
        var casoDeUso = new CrearHabito(new FakeUsuarioRepository(null), habitos);

        var resultado = await casoDeUso.EjecutarAsync(CrearSolicitud());

        Assert.Null(resultado);
        Assert.Null(habitos.HabitoPersistido);
        Assert.Equal(0, habitos.CantidadDeInserciones);
    }

    [Fact]
    public async Task EjecutarAsync_ConDatosValidos_DevuelveTodosLosDatosDelHabitoCreado()
    {
        var usuario = CrearUsuario();
        var habitos = new FakeHabitoRepository();
        var casoDeUso = new CrearHabito(
            new FakeUsuarioRepository(usuario),
            habitos);
        var solicitud = CrearSolicitud(usuario.Id);

        var resultado = Assert.IsType<HabitoCreado>(
            await casoDeUso.EjecutarAsync(solicitud));
        var persistido = Assert.IsType<Habito>(habitos.HabitoPersistido);

        Assert.Equal(persistido.Id, resultado.Id);
        Assert.Equal(usuario.Id, resultado.UsuarioId);
        Assert.Equal(solicitud.Nombre, resultado.Nombre);
        Assert.Equal(solicitud.Descripcion, resultado.Descripcion);
        Assert.Equal(persistido.FechaCreacion, resultado.FechaCreacion);
        Assert.Equal(solicitud.FechaInicio, resultado.FechaInicio);
        Assert.Equal(EstadoHabito.Activo, resultado.Estado);
        Assert.Equal(solicitud.DiasProgramados, resultado.DiasProgramados);
    }

    private static Usuario CrearUsuario() => new("Ana", "ana@example.com", "hashed-value");

    private static CrearHabitoRequest CrearSolicitud(
        Guid? usuarioId = null,
        string nombre = "Leer",
        IReadOnlyCollection<DayOfWeek>? diasProgramados = null)
    {
        return new CrearHabitoRequest(
            usuarioId ?? CrearUsuario().Id,
            nombre,
            "Leer un libro",
            new DateOnly(2026, 10, 6),
            diasProgramados ?? [DayOfWeek.Monday, DayOfWeek.Wednesday]);
    }

    private sealed class FakeUsuarioRepository(Usuario? usuario) : IUsuarioRepository
    {
        public Guid? IdBuscado { get; private set; }

        public CancellationToken CancellationTokenRecibido { get; private set; }

        public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken)
        {
            IdBuscado = id;
            CancellationTokenRecibido = cancellationToken;
            return Task.FromResult<Usuario?>(usuario?.Id == id ? usuario : null);
        }

        public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken cancellationToken)
        {
            return Task.FromResult<Usuario?>(null);
        }

        public Task<bool> ExistePorEmailAsync(string email, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<bool> IntentarAgregarAsync(Usuario usuario, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeHabitoRepository : IHabitoRepository
    {
        public Habito? HabitoPersistido { get; private set; }

        public int CantidadDeInserciones { get; private set; }

        public CancellationToken CancellationTokenRecibido { get; private set; }

        public Task AgregarAsync(Habito habito, CancellationToken cancellationToken)
        {
            CantidadDeInserciones++;
            HabitoPersistido = habito;
            CancellationTokenRecibido = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Habito>> ListarActivosPorUsuarioAsync(
            Guid usuarioId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Habito?> BuscarPorIdYUsuarioAsync(
            Guid habitId,
            Guid usuarioId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
