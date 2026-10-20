using Constia.Application.Habitos;
using Constia.Domain;

namespace Constia.Tests;

public sealed class ListarIconosHabitoTests
{
    [Fact]
    public void Ejecutar_ProyectaLosValoresDeDomainAlContratoDeSalida()
    {
        var resultado = new ListarIconosHabito().Ejecutar();

        Assert.Equal(16, resultado.Count);
        Assert.All(resultado, icono => Assert.IsType<IconoHabitoListado>(icono));
        Assert.Equal(CatalogoIconosHabito.Todos.Select(icono => (icono.Id, icono.Nombre, icono.Orden)),
            resultado.Select(icono => (icono.Id, icono.Nombre, icono.Orden)));
        Assert.Equal(CatalogoIconosHabitoTests.Esperados,
            resultado.Select(icono => (icono.Id, icono.Nombre, icono.Orden)).ToArray());
    }

    [Fact]
    public void Ejecutar_ModificarLaProyeccionNoAlteraElCatalogoCompartido()
    {
        var casoDeUso = new ListarIconosHabito();
        var primeraLectura = casoDeUso.Ejecutar();
        var lista = Assert.IsAssignableFrom<IList<IconoHabitoListado>>(primeraLectura);
        lista[0] = new IconoHabitoListado("desconocido", "Ajeno", 99);

        var segundaLectura = casoDeUso.Ejecutar();

        Assert.Equal(CatalogoIconosHabitoTests.Esperados,
            segundaLectura.Select(icono => (icono.Id, icono.Nombre, icono.Orden)).ToArray());
        Assert.Equal("libro", CatalogoIconosHabito.Todos[0].Id);
    }
}
