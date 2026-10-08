using Constia.Domain;

namespace Constia.Tests;

public sealed class CumplimientoTests
{
    [Fact]
    public void Constructor_ConValoresValidos_CreaCumplimientoConIdentidadHabitoYFecha()
    {
        var habito = CrearHabito();
        var fecha = new DateOnly(2026, 10, 7);

        var cumplimiento = new Cumplimiento(habito, fecha);

        Assert.NotEqual(Guid.Empty, cumplimiento.Id);
        Assert.Same(habito, cumplimiento.Habito);
        Assert.Equal(fecha, cumplimiento.Fecha);
    }

    [Fact]
    public void Constructor_CreaIdentidadUnicaParaCadaCumplimiento()
    {
        var habito = CrearHabito();

        var primero = new Cumplimiento(habito, new DateOnly(2026, 10, 7));
        var segundo = new Cumplimiento(habito, new DateOnly(2026, 10, 8));

        Assert.NotEqual(primero.Id, segundo.Id);
    }

    [Fact]
    public void Constructor_ConHabitoNulo_LanzaArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new Cumplimiento(null!, new DateOnly(2026, 10, 7)));
    }

    [Fact]
    public void Constructor_PermiteRepresentarCumplimientoDeHabitoInactivo()
    {
        var habito = CrearHabito();
        habito.Desactivar();

        var cumplimiento = new Cumplimiento(habito, new DateOnly(2026, 10, 7));

        Assert.Same(habito, cumplimiento.Habito);
        Assert.Equal(EstadoHabito.Inactivo, cumplimiento.Habito.Estado);
    }

    private static Habito CrearHabito() => new(
        new Usuario("Ana", "ana@example.com", "hashed-value", "Etc/UTC"),
        "Leer",
        null,
        new DateOnly(2026, 10, 5),
        [DayOfWeek.Monday]);
}
