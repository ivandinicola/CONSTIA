namespace Constia.Application.Habitos;

public interface ICategoriaHabitoReadRepository
{
    Task<IReadOnlyList<CategoriaHabitoListado>> ListarAsync(CancellationToken cancellationToken);
}
