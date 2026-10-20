namespace Constia.Application.Habitos;

public sealed class ListarCategoriasHabito(ICategoriaHabitoReadRepository categorias)
{
    public Task<IReadOnlyList<CategoriaHabitoListado>> EjecutarAsync(
        CancellationToken cancellationToken = default) =>
        categorias.ListarAsync(cancellationToken);
}
