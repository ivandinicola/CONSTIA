namespace Constia.Application.Habitos;

public interface ICategoriaHabitoReadRepository
{
    Task<bool> ExisteAsync(Guid categoriaId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CategoriaHabitoListado>> ListarAsync(CancellationToken cancellationToken);
}
