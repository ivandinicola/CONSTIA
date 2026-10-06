using Constia.Domain;

namespace Constia.Tests;

public class HabitoTests
{
    [Fact]
    public void Constructor_WithValidValues_CreatesActiveHabitoWithUniqueIdentifier()
    {
        var usuario = CrearUsuario();
        var antesDeCrear = DateTimeOffset.UtcNow;

        var habito = new Habito(
            usuario,
            "Leer",
            "Leer un libro",
            new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);

        var despuesDeCrear = DateTimeOffset.UtcNow;
        var otroHabito = new Habito(usuario, "Caminar", null, new DateOnly(2026, 10, 5), [DayOfWeek.Friday]);

        Assert.NotEqual(Guid.Empty, habito.Id);
        Assert.NotEqual(habito.Id, otroHabito.Id);
        Assert.Same(usuario, habito.Usuario);
        Assert.Equal("Leer", habito.Nombre);
        Assert.Equal("Leer un libro", habito.Descripcion);
        Assert.InRange(habito.FechaCreacion, antesDeCrear, despuesDeCrear);
        Assert.Equal(new DateOnly(2026, 10, 5), habito.FechaInicio);
        Assert.Equal(EstadoHabito.Activo, habito.Estado);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], habito.DiasProgramados.Select(dia => dia.Dia));
        Assert.Null(otroHabito.Descripcion);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WithEmptyOrWhitespaceName_ThrowsArgumentException(string nombre)
    {
        Assert.Throws<ArgumentException>(() => new Habito(
            CrearUsuario(), nombre, null, new DateOnly(2026, 10, 5), [DayOfWeek.Monday]));
    }

    [Fact]
    public void Constructor_WithNullUsuario_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new Habito(
            null!, "Leer", null, new DateOnly(2026, 10, 5), [DayOfWeek.Monday]));
    }

    [Fact]
    public void Constructor_WithNoScheduledDays_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Habito(
            CrearUsuario(), "Leer", null, new DateOnly(2026, 10, 5), []));
    }

    [Fact]
    public void Constructor_WithInvalidScheduledDay_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Habito(
            CrearUsuario(), "Leer", null, new DateOnly(2026, 10, 5), [(DayOfWeek)7]));
    }

    [Fact]
    public void Constructor_WithRepeatedScheduledDays_StoresEachDayOnce()
    {
        var habito = new Habito(
            CrearUsuario(), "Leer", null, new DateOnly(2026, 10, 5), [DayOfWeek.Monday, DayOfWeek.Monday]);

        Assert.Equal([DayOfWeek.Monday], habito.DiasProgramados.Select(dia => dia.Dia));
    }

    [Fact]
    public void Desactivar_SetsInactiveAndPreservesHabitData()
    {
        var usuario = CrearUsuario();
        var habito = new Habito(
            usuario,
            "Leer",
            "Leer un libro",
            new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);

        habito.Desactivar();

        Assert.Equal(EstadoHabito.Inactivo, habito.Estado);
        Assert.Same(usuario, habito.Usuario);
        Assert.Equal("Leer", habito.Nombre);
        Assert.Equal("Leer un libro", habito.Descripcion);
        Assert.Equal(new DateOnly(2026, 10, 5), habito.FechaInicio);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], habito.DiasProgramados.Select(dia => dia.Dia));
    }

    private static Usuario CrearUsuario() => new("Ana", "ana@example.com", "hashed-value");
}
