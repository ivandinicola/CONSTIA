using Constia.Application.Habitos;
using Constia.Domain;

namespace Constia.Tests;

public sealed class HabitoMetadataTests
{
    [Fact]
    public void Constructor_ConservaMetadatosNullYLasReglasTemporalesExistentes()
    {
        var usuario = new Usuario("Prueba", "metadata@example.invalid", "test-hash", "Etc/UTC");
        var inicio = new DateOnly(2026, 10, 1);
        var habito = new Habito(usuario, "Leer", null, inicio, [DayOfWeek.Monday]);
        var creacion = habito.FechaCreacion;
        var icono = typeof(Habito).GetProperty("IconoId");
        Assert.NotNull(icono);
        Assert.Equal(typeof(string), icono.PropertyType);
        Assert.False(icono.SetMethod!.IsPublic);
        Assert.Null(icono.GetValue(habito));
        Assert.Null(habito.CategoriaId);

        habito.ActualizarConfiguracionVersionada("Leer más", null, [DayOfWeek.Monday], inicio.AddDays(1));
        habito.Desactivar(inicio.AddDays(2));

        Assert.Null(icono.GetValue(habito));
        Assert.Null(habito.CategoriaId);
        Assert.Equal(inicio, habito.FechaInicio);
        Assert.Equal(creacion, habito.FechaCreacion);
        Assert.Single(habito.Programaciones);
        Assert.Equal(2, habito.PeriodosEstado.Count);
        Assert.Equal(EstadoHabito.Activo, habito.ObtenerEstadoPara(inicio));
        Assert.Equal(EstadoHabito.Inactivo, habito.ObtenerEstadoPara(inicio.AddDays(2)));
    }

    [Theory]
    [InlineData(typeof(HabitoCreado))]
    [InlineData(typeof(HabitoListado))]
    public void Resultados_ExponenAmbosMetadatosSinCambiarLosTiposPrevios(Type tipo)
    {
        var categoria = tipo.GetProperty("CategoriaId");
        var icono = tipo.GetProperty("IconoId");
        Assert.NotNull(categoria);
        Assert.NotNull(icono);
        Assert.Equal(typeof(Guid?), categoria.PropertyType);
        Assert.Equal(typeof(string), icono.PropertyType);
        Assert.Equal(typeof(DateOnly), tipo.GetProperty("FechaInicio")!.PropertyType);
        Assert.Equal(typeof(DateTimeOffset), tipo.GetProperty("FechaCreacion")!.PropertyType);
    }
}
