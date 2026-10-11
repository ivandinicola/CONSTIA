using Constia.Application.Habitos;
using Constia.Application.Temporal;
using Constia.Application.Usuarios;
using Constia.Domain;

namespace Constia.Tests;

public sealed class EditarHabitoTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 10);

    [Fact]
    public async Task EjecutarAsync_ConCambioDeDias_UsaZonaDelPropietarioUnaVezYPersisteVersion()
    {
        var usuario = CrearUsuario("America/Argentina/Buenos_Aires");
        var habito = CrearHabito(usuario);
        var (useCase, repository, fechaLocal) = CrearCaso(habito, usuario, Hoy);
        using var cancellation = new CancellationTokenSource();

        var result = await useCase.EjecutarAsync(
            habito.Id,
            usuario.Id,
            new EditarHabitoRequest("Caminar", "Después", [DayOfWeek.Friday]),
            cancellation.Token);

        Assert.Equal(usuario.TimeZoneId, fechaLocal.ZonaSolicitada);
        Assert.Equal(1, fechaLocal.CantidadDeCalculos);
        Assert.Equal(cancellation.Token, repository.CancellationTokenBusqueda);
        Assert.Equal(cancellation.Token, repository.CancellationTokenGuardado);
        Assert.Equal(1, repository.CantidadDeGuardados);
        Assert.Equal("Caminar", habito.Nombre);
        Assert.Equal("Después", habito.Descripcion);
        Assert.Equal(Hoy, Assert.Single(habito.Programaciones, programacion => programacion.VigenteHasta is null).VigenteDesde);
        Assert.Equal([DayOfWeek.Friday], habito.DiasProgramados.Select(dia => dia.Dia));
        Assert.NotNull(result);
        Assert.Equal(habito.Id, result.Id);
        Assert.Equal([DayOfWeek.Friday], result.DiasProgramados);
    }

    [Fact]
    public async Task EjecutarAsync_SoloMetadatosYMismosDias_PermiteEdicionSinCrearVersion()
    {
        var usuario = CrearUsuario();
        var habito = CrearHabito(usuario);
        var programacionOriginal = Assert.Single(habito.Programaciones);
        var (useCase, repository, _) = CrearCaso(habito, usuario, Hoy.AddDays(-2));

        var result = await useCase.EjecutarAsync(
            habito.Id,
            usuario.Id,
            new EditarHabitoRequest("Nuevo nombre", "Nueva descripción", [DayOfWeek.Wednesday, DayOfWeek.Monday]));

        Assert.NotNull(result);
        Assert.Equal("Nuevo nombre", habito.Nombre);
        Assert.Equal("Nueva descripción", habito.Descripcion);
        Assert.Same(programacionOriginal, Assert.Single(habito.Programaciones));
        Assert.Equal(1, repository.CantidadDeGuardados);
    }

    [Fact]
    public async Task EjecutarAsync_ConDiasInvalidos_NoMutaNiGuarda()
    {
        var usuario = CrearUsuario();
        var habito = CrearHabito(usuario);
        var (useCase, repository, _) = CrearCaso(habito, usuario, Hoy);

        await Assert.ThrowsAsync<ArgumentException>(() => useCase.EjecutarAsync(
            habito.Id,
            usuario.Id,
            new EditarHabitoRequest("Cambio", "Cambio", [])));

        Assert.Equal("Leer", habito.Nombre);
        Assert.Equal("Descripción original", habito.Descripcion);
        Assert.Single(habito.Programaciones);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], habito.DiasProgramados.Select(dia => dia.Dia));
        Assert.Equal(0, repository.CantidadDeGuardados);
    }

    [Fact]
    public async Task EjecutarAsync_ConFechaRetroactivaYCambioDeDias_LanzaConflictoSinMutar()
    {
        var usuario = CrearUsuario();
        var habito = CrearHabito(usuario);
        habito.ActualizarConfiguracionVersionada("Leer", "Descripción original", [DayOfWeek.Friday], Hoy);
        var (useCase, repository, _) = CrearCaso(habito, usuario, Hoy.AddDays(-1));

        await Assert.ThrowsAsync<ConflictoVigenciaProgramacionException>(() => useCase.EjecutarAsync(
            habito.Id,
            usuario.Id,
            new EditarHabitoRequest("Cambio", "Cambio", [DayOfWeek.Saturday])));

        Assert.Equal("Leer", habito.Nombre);
        Assert.Equal("Descripción original", habito.Descripcion);
        Assert.Equal(2, habito.Programaciones.Count);
        Assert.Equal([DayOfWeek.Friday], habito.DiasProgramados.Select(dia => dia.Dia));
        Assert.Equal(0, repository.CantidadDeGuardados);
    }

    [Fact]
    public async Task EjecutarAsync_FechaRetroactivaConDiasIdenticos_PermiteActualizarMetadatos()
    {
        var usuario = CrearUsuario();
        var habito = CrearHabito(usuario);
        habito.ActualizarConfiguracionVersionada("Leer", "Descripción original", [DayOfWeek.Friday], Hoy);
        var (useCase, repository, _) = CrearCaso(habito, usuario, Hoy.AddDays(-1));

        var result = await useCase.EjecutarAsync(
            habito.Id,
            usuario.Id,
            new EditarHabitoRequest("Cambio", "Actualizada", [DayOfWeek.Friday]));

        Assert.NotNull(result);
        Assert.Equal("Cambio", habito.Nombre);
        Assert.Equal("Actualizada", habito.Descripcion);
        Assert.Equal(2, habito.Programaciones.Count);
        Assert.Equal(1, repository.CantidadDeGuardados);
    }

    [Fact]
    public async Task EjecutarAsync_ConHábitoInactivoPermiteVersionarLosDias()
    {
        var usuario = CrearUsuario();
        var habito = CrearHabito(usuario);
        habito.Desactivar();
        var (useCase, _, _) = CrearCaso(habito, usuario, Hoy);

        var result = await useCase.EjecutarAsync(
            habito.Id,
            usuario.Id,
            new EditarHabitoRequest("Cambio", null, [DayOfWeek.Friday]));

        Assert.NotNull(result);
        Assert.Equal(EstadoHabito.Inactivo, habito.Estado);
        Assert.Equal([DayOfWeek.Friday], habito.DiasProgramados.Select(dia => dia.Dia));
    }

    [Fact]
    public async Task EjecutarAsync_ZonaHorariaPersistidaInvalida_LanzaErrorIdentificableSinGuardar()
    {
        var usuario = CrearUsuario("Invalid/Unknown");
        var habito = CrearHabito(usuario);
        var repository = new FakeHabitoRepository(habito);
        var useCase = new EditarHabito(
            repository,
            new FakeUsuarioRepository(usuario),
            new FechaLocalUsuario(
                new TimeProviderFijo(DateTimeOffset.Parse("2026-10-10T12:00:00Z")),
                new ZonaHorariaIana()));

        await Assert.ThrowsAsync<ZonaHorariaPersistidaInvalidaException>(() => useCase.EjecutarAsync(
            habito.Id,
            usuario.Id,
            new EditarHabitoRequest("Cambio", "Cambio", [DayOfWeek.Friday])));

        Assert.Equal("Leer", habito.Nombre);
        Assert.Equal(0, repository.CantidadDeGuardados);
    }

    [Fact]
    public async Task EjecutarAsync_SinCoincidencia_DevuelveNullYSinCalcularFechaNiGuardar()
    {
        var usuario = CrearUsuario();
        var (useCase, repository, fechaLocal) = CrearCaso(null, usuario, Hoy);

        var result = await useCase.EjecutarAsync(
            Guid.NewGuid(),
            usuario.Id,
            new EditarHabitoRequest("Caminar", null, [DayOfWeek.Friday]));

        Assert.Null(result);
        Assert.Equal(0, repository.CantidadDeGuardados);
        Assert.Equal(0, fechaLocal.CantidadDeCalculos);
    }

    [Fact]
    public async Task EjecutarAsync_ConservaCumplimientosExistentes()
    {
        var usuario = CrearUsuario();
        var habito = CrearHabito(usuario);
        var cumplimiento = new Cumplimiento(habito, new DateOnly(2026, 10, 7));
        var (useCase, _, _) = CrearCaso(habito, usuario, Hoy);

        await useCase.EjecutarAsync(
            habito.Id,
            usuario.Id,
            new EditarHabitoRequest("Caminar", "Después", [DayOfWeek.Friday]));

        Assert.Same(habito, cumplimiento.Habito);
        Assert.Equal(new DateOnly(2026, 10, 7), cumplimiento.Fecha);
    }

    private static (EditarHabito UseCase, FakeHabitoRepository Repository, FakeFechaLocalUsuario FechaLocal)
        CrearCaso(Habito? habito, Usuario usuario, DateOnly fecha)
    {
        var repository = new FakeHabitoRepository(habito);
        var fechaLocal = new FakeFechaLocalUsuario(fecha);
        return (
            new EditarHabito(repository, new FakeUsuarioRepository(usuario), fechaLocal),
            repository,
            fechaLocal);
    }

    private static Usuario CrearUsuario(string timeZoneId = "Etc/UTC") =>
        new("Ana", "ana@example.com", "hashed-value", timeZoneId);

    private static Habito CrearHabito(Usuario usuario) =>
        new(usuario, "Leer", "Descripción original", new DateOnly(2026, 10, 1),
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);

    private sealed class FakeHabitoRepository(Habito? habit) : IHabitoRepository
    {
        public CancellationToken CancellationTokenBusqueda { get; private set; }

        public CancellationToken CancellationTokenGuardado { get; private set; }

        public int CantidadDeGuardados { get; private set; }

        public Habito? HabitoGuardado { get; private set; }

        public Task AgregarAsync(Habito habito, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Habito>> ListarActivosPorUsuarioAsync(
            Guid usuarioId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Habito>> ListarInactivosPorUsuarioAsync(
            Guid usuarioId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Habito?> BuscarPorIdYUsuarioAsync(
            Guid habitId, Guid usuarioId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Habito?> BuscarParaModificarPorIdYUsuarioAsync(
            Guid habitId, Guid usuarioId, CancellationToken cancellationToken)
        {
            CancellationTokenBusqueda = cancellationToken;
            return Task.FromResult<Habito?>(
                habit is not null && habit.Id == habitId && habit.Usuario.Id == usuarioId ? habit : null);
        }

        public Task GuardarCambiosAsync(Habito habito, CancellationToken cancellationToken)
        {
            CantidadDeGuardados++;
            HabitoGuardado = habito;
            CancellationTokenGuardado = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUsuarioRepository(Usuario usuario) : IUsuarioRepository
    {
        public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Usuario?>(id == usuario.Id ? usuario : null);

        public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistePorEmailAsync(string email, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> IntentarAgregarAsync(Usuario usuario, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task GuardarCambiosAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeFechaLocalUsuario(DateOnly fecha) : IFechaLocalUsuario
    {
        public string? ZonaSolicitada { get; private set; }

        public int CantidadDeCalculos { get; private set; }

        public DateOnly ObtenerHoy(string timeZoneId)
        {
            ZonaSolicitada = timeZoneId;
            CantidadDeCalculos++;
            return fecha;
        }
    }

    private sealed class TimeProviderFijo(DateTimeOffset instanteUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instanteUtc;
    }
}
