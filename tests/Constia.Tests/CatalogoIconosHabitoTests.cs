using Constia.Domain;

namespace Constia.Tests;

public sealed class CatalogoIconosHabitoTests
{
    internal static readonly (string Id, string Nombre, int Orden)[] Esperados =
    [
        ("libro", "Libro", 1),
        ("fuerza", "Fuerza", 2),
        ("agua", "Agua", 3),
        ("loto", "Loto", 4),
        ("manzana", "Manzana", 5),
        ("luna", "Luna", 6),
        ("cafe", "Café", 7),
        ("corredor", "Corredor", 8),
        ("corazon", "Corazón", 9),
        ("cerebro", "Cerebro", 10),
        ("sol", "Sol", 11),
        ("huella", "Huella", 12),
        ("brote", "Brote", 13),
        ("portatil", "Portátil", 14),
        ("musica", "Música", 15),
        ("billete", "Billete", 16)
    ];

    public static IEnumerable<object[]> IdentificadoresOficiales =>
        Esperados.Select(icono => new object[] { icono.Id });

    [Fact]
    public void Todos_ExponeLosDieciseisValoresExactosEnElOrdenAprobado()
    {
        Assert.Equal(Esperados, CatalogoIconosHabito.Todos
            .Select(icono => (icono.Id, icono.Nombre, icono.Orden)).ToArray());
        Assert.Equal(16, CatalogoIconosHabito.Todos.Select(icono => icono.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(16, CatalogoIconosHabito.Todos.Select(icono => icono.Orden).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(IdentificadoresOficiales))]
    public void EsOficial_ClaveExacta_DevuelveTrue(string id)
    {
        Assert.True(CatalogoIconosHabito.EsOficial(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Libro")]
    [InlineData("LIBRO")]
    [InlineData(" libro")]
    [InlineData("libro ")]
    [InlineData("café")]
    [InlineData("cafe\u0301")]
    [InlineData("movimiento")]
    [InlineData("meditacion")]
    [InlineData("alimentacion")]
    [InlineData("constia-lectura.png")]
    [InlineData("desconocido")]
    public void EsOficial_ClaveDistinta_NoNormalizaNiAceptaAliases(string? id)
    {
        Assert.False(CatalogoIconosHabito.EsOficial(id));
    }

    [Fact]
    public void Todos_NoPermiteModificarLaColeccionNiSusElementos()
    {
        var lista = Assert.IsAssignableFrom<IList<IconoHabito>>(CatalogoIconosHabito.Todos);
        var primero = Assert.Single(lista, icono => icono.Id == "libro");

        Assert.Throws<NotSupportedException>(() => lista.Add(primero));
        Assert.Throws<NotSupportedException>(() => lista.Clear());
        Assert.Throws<NotSupportedException>(() => { lista[0] = lista[1]; });
        Assert.Null(typeof(IconoHabito).GetProperty(nameof(IconoHabito.Id))!.SetMethod);
        Assert.Null(typeof(IconoHabito).GetProperty(nameof(IconoHabito.Nombre))!.SetMethod);
        Assert.Null(typeof(IconoHabito).GetProperty(nameof(IconoHabito.Orden))!.SetMethod);
        Assert.Equal(Esperados, CatalogoIconosHabito.Todos
            .Select(icono => (icono.Id, icono.Nombre, icono.Orden)).ToArray());
    }

    [Fact]
    public void Catalogos_ConservanIdentidadesIndependientes()
    {
        var categorias = CatalogoCategoriasHabito.Todas
            .Select(categoria => (categoria.Id, categoria.Nombre, categoria.Orden)).ToArray();

        foreach (var categoria in CatalogoCategoriasHabito.Todas)
        {
            Assert.False(CatalogoIconosHabito.EsOficial(categoria.Id.ToString()));
        }

        Assert.Equal(10, categorias.Length);
        Assert.Equal(16, CatalogoIconosHabito.Todos.Count);
        Assert.Equal(categorias, CatalogoCategoriasHabito.Todas
            .Select(categoria => (categoria.Id, categoria.Nombre, categoria.Orden)).ToArray());
    }
}
