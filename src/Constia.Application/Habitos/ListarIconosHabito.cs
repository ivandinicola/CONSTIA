using Constia.Domain;

namespace Constia.Application.Habitos;

public sealed class ListarIconosHabito
{
    public IReadOnlyList<IconoHabitoListado> Ejecutar() =>
        CatalogoIconosHabito.Todos
            .Select(icono => new IconoHabitoListado(icono.Id, icono.Nombre, icono.Orden))
            .ToArray();
}
