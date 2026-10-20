using System.Text.Json;
using Constia.Application.Habitos;
using Constia.Application.Temporal;
using Constia.Domain;

namespace Constia.Tests;

public sealed partial class EditarHabitoTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Metadatos_AsignacionIndependienteYPersistenciaSinVersionar(bool category, bool icon, bool inactive)
    {
        var user = CrearUsuario();
        var habit = CrearHabito(user);
        if (inactive) habit.Desactivar(habit.FechaInicio);
        var created = habit.FechaCreacion;
        var schedule = Assert.Single(habit.Programaciones);
        var periods = habit.PeriodosEstado.ToArray();
        var completion = new Cumplimiento(habit, habit.FechaInicio);
        var (useCase, repository, _) = CrearCaso(habit, user, Hoy);
        var cat = category ? CatalogoCategoriasHabito.Todas[4].Id : (Guid?)null;
        var ico = icon ? "billete" : null;
        var result = await useCase.EjecutarAsync(habit.Id, user.Id,
            new EditarHabitoRequest(habit.Nombre, habit.Descripcion, [DayOfWeek.Monday, DayOfWeek.Wednesday], cat, ico));
        Assert.NotNull(result);
        Assert.Equal(cat, result.CategoriaId);
        Assert.Equal(ico, result.IconoId);
        Assert.Equal(cat, habit.CategoriaId);
        Assert.Equal(ico, habit.IconoId);
        Assert.Same(schedule, Assert.Single(habit.Programaciones));
        Assert.Equal(periods, habit.PeriodosEstado);
        Assert.Equal(created, habit.FechaCreacion);
        Assert.Equal(new DateOnly(2026,10,1), habit.FechaInicio);
        Assert.Equal(inactive ? EstadoHabito.Inactivo : EstadoHabito.Activo, habit.Estado);
        Assert.Equal(habit.FechaInicio, completion.Fecha);
        Assert.Equal(1, repository.CantidadDeGuardados);
    }

    [Theory]
    [InlineData("empty", true)]
    [InlineData("unknown", true)]
    [InlineData("missing", true)]
    [InlineData("icon", true)]
    [InlineData("days", true)]
    [InlineData("enum", true)]
    [InlineData("name", true)]
    [InlineData("zone", true)]
    [InlineData("conflict", true)]
    public async Task EntradaInvalida_NoDejaNingunaMutacion(string scenario, bool assigned)
    {
        var user = CrearUsuario(scenario == "zone" ? "Invalid/Unknown" : "Etc/UTC");
        var habit = CrearHabito(user);
        if (assigned) habit.ActualizarMetadatos(CatalogoCategoriasHabito.Todas[0].Id, "libro");
        if (scenario == "conflict")
            habit.ActualizarConfiguracionVersionada(habit.Nombre, habit.Descripcion, [DayOfWeek.Friday], Hoy);
        var before = MetadataSnapshot(habit);
        var repository = new FakeHabitoRepository(habit);
        var categories = new CategoriasConExistencia(scenario != "missing");
        IFechaLocalUsuario clock = scenario == "zone"
            ? new FechaLocalUsuario(new TimeProviderFijo(DateTimeOffset.Parse("2026-10-10T12:00:00Z")), new ZonaHorariaIana())
            : new FakeFechaLocalUsuario(scenario == "conflict" ? Hoy.AddDays(-1) : Hoy);
        var useCase = new EditarHabito(repository, new FakeUsuarioRepository(user), clock, categories);
        Guid? cat = scenario switch {
            "empty" => Guid.Empty,
            "unknown" => Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000099"),
            _ => CatalogoCategoriasHabito.Todas[4].Id
        };
        var request = new EditarHabitoRequest(scenario == "name" ? " " : "Cambio", "Cambio",
            scenario == "days" ? [] : scenario == "enum" ? [(DayOfWeek)99] : [DayOfWeek.Saturday], cat, scenario == "icon" ? "Libro" : "billete");
        if (scenario == "zone")
            await Assert.ThrowsAsync<ZonaHorariaPersistidaInvalidaException>(() => useCase.EjecutarAsync(habit.Id, user.Id, request));
        else if (scenario == "conflict")
            await Assert.ThrowsAsync<ConflictoVigenciaProgramacionException>(() => useCase.EjecutarAsync(habit.Id, user.Id, request));
        else if (scenario is "days" or "enum" or "name")
            await Assert.ThrowsAsync<ArgumentException>(() => useCase.EjecutarAsync(habit.Id, user.Id, request));
        else
        {
            var error = await Assert.ThrowsAsync<MetadatosHabitoInvalidosException>(() => useCase.EjecutarAsync(habit.Id, user.Id, request));
            Assert.Equal(scenario == "icon" ? "iconoId" : "categoriaId", error.Campo);
        }
        Assert.Equal(before, MetadataSnapshot(habit));
        Assert.Equal(0, repository.CantidadDeGuardados);
        if (scenario is "empty" or "unknown") Assert.Equal(0, categories.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BusquedaAjenaOInexistente_NoValidaCatalogoNiGuarda(bool foreign)
    {
        var owner = CrearUsuario();
        var habit = foreign ? CrearHabito(owner) : null;
        var repository = new FakeHabitoRepository(habit);
        var categories = new CategoriasConExistencia(true);
        var useCase = new EditarHabito(repository, new FakeUsuarioRepository(owner), new FakeFechaLocalUsuario(Hoy), categories);
        Assert.Null(await useCase.EjecutarAsync(habit?.Id ?? Guid.NewGuid(), Guid.NewGuid(),
            new EditarHabitoRequest("Cambio", null, [DayOfWeek.Monday], Guid.Empty, "incorrecto")));
        Assert.Equal(0, categories.Calls);
        Assert.Equal(0, repository.CantidadDeGuardados);
    }

    [Fact]
    public async Task CategoriaExistencia_PropagaCancellationToken_YReemplazaConservandoOmitido()
    {
        var user = CrearUsuario();
        var habit = CrearHabito(user);
        habit.ActualizarMetadatos(CatalogoCategoriasHabito.Todas[0].Id, "libro");
        var repository = new FakeHabitoRepository(habit);
        var categories = new CategoriasConExistencia(true);
        var useCase = new EditarHabito(repository, new FakeUsuarioRepository(user), new FakeFechaLocalUsuario(Hoy), categories);
        using var cancellation = new CancellationTokenSource();
        await useCase.EjecutarAsync(habit.Id, user.Id, new EditarHabitoRequest(habit.Nombre, habit.Descripcion,
            [DayOfWeek.Monday, DayOfWeek.Wednesday], CatalogoCategoriasHabito.Todas[1].Id), cancellation.Token);
        Assert.Equal(cancellation.Token, categories.Token);
        Assert.Equal(CatalogoCategoriasHabito.Todas[1].Id, categories.Id);
        Assert.Equal("libro", habit.IconoId);
        await useCase.EjecutarAsync(habit.Id, user.Id, new EditarHabitoRequest(habit.Nombre, habit.Descripcion,
            [DayOfWeek.Monday, DayOfWeek.Wednesday], IconoId: "agua"));
        Assert.Equal(CatalogoCategoriasHabito.Todas[1].Id, habit.CategoriaId);
        Assert.Equal("agua", habit.IconoId);
        Assert.Single(habit.Programaciones);
    }

    private static string MetadataSnapshot(Habito h) => JsonSerializer.Serialize(new {
        h.Id, h.Nombre, h.Descripcion, h.CategoriaId, h.IconoId, h.FechaInicio, h.FechaCreacion, h.Estado,
        Programaciones = h.Programaciones.Select(p => new {p.VigenteDesde, p.VigenteHasta, Dias=p.DiasProgramados.Select(d=>d.Dia)}),
        Periodos = h.PeriodosEstado.Select(p=>new {p.VigenteDesde,p.VigenteHasta,p.Estado})
    });

    private sealed class CategoriasConExistencia(bool exists) : ICategoriaHabitoReadRepository
    {
        public int Calls { get; private set; }
        public Guid Id { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<bool> ExisteAsync(Guid id, CancellationToken token)
        {
            Calls++; Id=id; Token=token;
            token.ThrowIfCancellationRequested();
            return Task.FromResult(exists);
        }
        public Task<IReadOnlyList<CategoriaHabitoListado>> ListarAsync(CancellationToken token) => throw new NotSupportedException();
    }
}
