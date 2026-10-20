using Constia.Domain;

namespace Constia.Tests;

public sealed class HabitoActualizarMetadatosTests
{
    public static IEnumerable<object[]> Oficiales() =>
        CatalogoCategoriasHabito.Todas.SelectMany(c => CatalogoIconosHabito.Todos.Select(i => new object[] { c.Id, i.Id }));

    [Theory]
    [MemberData(nameof(Oficiales))]
    public void AsignarOReemplazar_ConservaHistoriaYValoresOmitidos(Guid category, string icon)
    {
        var habit = NewHabit();
        var start = habit.FechaInicio;
        var created = habit.FechaCreacion;
        var schedule = Assert.Single(habit.Programaciones);
        var periods = habit.PeriodosEstado.ToArray();
        var completion = new Cumplimiento(habit, start);
        habit.ActualizarMetadatos(category, icon);
        habit.ActualizarMetadatos(null, null);
        Assert.Equal(category, habit.CategoriaId);
        Assert.Equal(icon, habit.IconoId);
        habit.ActualizarMetadatos(CatalogoCategoriasHabito.Todas[0].Id, "agua");
        Assert.Equal(CatalogoCategoriasHabito.Todas[0].Id, habit.CategoriaId);
        Assert.Equal("agua", habit.IconoId);
        Assert.Equal(start, habit.FechaInicio);
        Assert.Equal(created, habit.FechaCreacion);
        Assert.Equal("Leer", habit.Nombre);
        Assert.Same(schedule, Assert.Single(habit.Programaciones));
        Assert.Equal(periods, habit.PeriodosEstado);
        Assert.Equal(EstadoHabito.Activo, habit.Estado);
        Assert.Same(habit, completion.Habito);
        Assert.Equal(start, completion.Fecha);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Libro")]
    [InlineData(" libro ")]
    [InlineData("desconocido")]
    public void IconoInvalido_NoAsignaNiLaCategoriaValida(string icon)
    {
        var habit = NewHabit();
        Assert.Throws<ArgumentException>(() => habit.ActualizarMetadatos(CatalogoCategoriasHabito.Todas[0].Id, icon));
        Assert.Null(habit.CategoriaId);
        Assert.Null(habit.IconoId);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("82c0b8e6-0b9d-4e18-9bf8-000000000099")]
    public void CategoriaInvalida_NoAsignaIconoValido(string category)
    {
        var habit = NewHabit();
        Assert.Throws<ArgumentException>(() => habit.ActualizarMetadatos(Guid.Parse(category), "libro"));
        Assert.Null(habit.CategoriaId);
        Assert.Null(habit.IconoId);
    }

    [Fact]
    public void Historico_OmitidosSinDefault_AsignacionesIndependientes()
    {
        var habit = NewHabit();
        habit.ActualizarMetadatos(null, null);
        Assert.Null(habit.CategoriaId);
        Assert.Null(habit.IconoId);
        habit.ActualizarMetadatos(null, "billete");
        Assert.Null(habit.CategoriaId);
        Assert.Equal("billete", habit.IconoId);
        habit.ActualizarMetadatos(CatalogoCategoriasHabito.Todas[4].Id, null);
        Assert.Equal(CatalogoCategoriasHabito.Todas[4].Id, habit.CategoriaId);
        Assert.Equal("billete", habit.IconoId);
    }

    private static Habito NewHabit() => HabitoHistorico.Crear(
        new Usuario("Test", "test@example.invalid", "test-hash", "Etc/UTC"),
        "Leer", "Historia", new DateOnly(2026, 10, 1), [DayOfWeek.Monday]);
}
