using Constia.Domain;
using Xunit;

namespace Constia.API.Tests;

public sealed class HabitoMetadataMaterializationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("libro", false)]
    [InlineData("libro", true)]
    [InlineData("LiBrO", true)]
    public void Ef_MaterializaLiteralmenteSinAsignacionNiNormalizacion(string? icono, bool conCategoria)
    {
        var user = new Usuario("Prueba", "ef@example.invalid", "test-hash", "Etc/UTC");
        Guid? category = conCategoria ? CatalogoCategoriasHabito.Todas[4].Id : null;

        var habit = HabitoMetadataMaterialization.Crear(user, category, icono);

        Assert.Equal(icono, habit.IconoId);
        Assert.Equal(category, habit.CategoriaId);
        Assert.Same(user, habit.Usuario);
        Assert.Equal(new DateOnly(2026, 10, 1), habit.FechaInicio);
        Assert.Single(habit.Programaciones);
        Assert.Equal([DayOfWeek.Monday], habit.DiasProgramados.Select(d => d.Dia));
        Assert.Equal(EstadoHabito.Activo, habit.ObtenerEstadoPara(habit.FechaInicio));
    }
}
