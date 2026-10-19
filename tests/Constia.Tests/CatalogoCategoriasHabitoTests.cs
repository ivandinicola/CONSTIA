using Constia.Domain;

namespace Constia.Tests;

public sealed class CatalogoCategoriasHabitoTests
{
    private static readonly (Guid Id, string Nombre, int Orden)[] Esperadas =
    [
        (Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000001"), "Salud y movimiento", 1),
        (Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000002"), "Alimentación e hidratación", 2),
        (Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000003"), "Descanso y sueño", 3),
        (Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000004"), "Bienestar emocional", 4),
        (Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000005"), "Aprendizaje y crecimiento", 5),
        (Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000006"), "Productividad y organización", 6),
        (Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000007"), "Relaciones y vínculos", 7),
        (Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000008"), "Finanzas personales", 8),
        (Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000009"), "Creatividad y ocio", 9),
        (Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000010"), "Hogar y vida cotidiana", 10)
    ];

    public static IEnumerable<object[]> IdentificadoresOficiales =>
        Esperadas.Select(categoria => new object[] { categoria.Id });

    [Fact]
    public void Todas_ExponeExactamenteLosDiezValoresOficialesEnOrden()
    {
        var categorias = CatalogoCategoriasHabito.Todas;

        Assert.Equal(
            Esperadas,
            categorias.Select(categoria => (categoria.Id, categoria.Nombre, categoria.Orden)).ToArray());
    }

    [Theory]
    [MemberData(nameof(IdentificadoresOficiales))]
    public void EsOficial_ConIdentificadorDelCatalogo_DevuelveTrue(Guid id)
    {
        Assert.True(CatalogoCategoriasHabito.EsOficial(id));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("82c0b8e6-0b9d-4e18-9bf8-000000000099")]
    public void EsOficial_ConIdentificadorVacioODesconocido_DevuelveFalse(string id)
    {
        Assert.False(CatalogoCategoriasHabito.EsOficial(Guid.Parse(id)));
    }

    [Fact]
    public void Todas_NoPermiteAgregarNiReemplazarCategoriasDesdeLaColeccion()
    {
        var categorias = CatalogoCategoriasHabito.Todas;
        var lista = Assert.IsAssignableFrom<IList<CategoriaHabito>>(categorias);
        var categoriaAjena = new CategoriaHabito(
            Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000099"),
            "Categoría ajena",
            11);

        Assert.Throws<NotSupportedException>(() => lista.Add(categoriaAjena));
        Assert.Throws<NotSupportedException>(() => { lista[0] = categoriaAjena; });
        Assert.Equal(
            Esperadas,
            CatalogoCategoriasHabito.Todas
                .Select(categoria => (categoria.Id, categoria.Nombre, categoria.Orden)).ToArray());
    }
}
