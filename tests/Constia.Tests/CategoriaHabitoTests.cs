using System.Reflection;
using Constia.Domain;

namespace Constia.Tests;

public sealed class CategoriaHabitoTests
{
    [Fact]
    public void Constructor_ConservaLaIdentidadIndependientementeDelNombreYOrden()
    {
        var id = Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000005");

        var categoria = new CategoriaHabito(id, "Aprendizaje y crecimiento", 5);
        var otraPresentacion = new CategoriaHabito(id, "Otro nombre", 20);

        Assert.Equal(id, categoria.Id);
        Assert.Equal(id, otraPresentacion.Id);
        Assert.Equal("Aprendizaje y crecimiento", categoria.Nombre);
        Assert.Equal(5, categoria.Orden);
        Assert.Equal("Otro nombre", otraPresentacion.Nombre);
        Assert.Equal(20, otraPresentacion.Orden);
    }

    [Fact]
    public void Constructor_ConIdentificadorVacio_RechazaLaCategoria()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new CategoriaHabito(Guid.Empty, "Salud y movimiento", 1));

        Assert.Equal("id", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void Constructor_ConNombreAusenteOVacio_RechazaLaCategoria(string? nombre)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => new CategoriaHabito(
                Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000001"),
                nombre!,
                1));

        Assert.Equal("nombre", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_ConOrdenNoPositivo_RechazaLaCategoria(int orden)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CategoriaHabito(
                Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000001"),
                "Salud y movimiento",
                orden));

        Assert.Equal("orden", exception.ParamName);
    }

    [Fact]
    public void Propiedades_NoPermitenEscrituraPublica()
    {
        var propiedades = typeof(CategoriaHabito).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

        Assert.All(propiedades, propiedad =>
            Assert.False(propiedad.SetMethod?.IsPublic ?? false));
    }
}
