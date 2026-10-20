using Constia.Application.Habitos;
using Microsoft.EntityFrameworkCore;

namespace Constia.Infrastructure.Habitos;

public sealed class CategoriaHabitoReadRepository(ConstiaDbContext dbContext) : ICategoriaHabitoReadRepository
{
    public Task<bool> ExisteAsync(Guid categoriaId, CancellationToken cancellationToken) =>
        dbContext.CategoriasHabito.AsNoTracking()
            .AnyAsync(categoria => categoria.Id == categoriaId, cancellationToken);

    public async Task<IReadOnlyList<CategoriaHabitoListado>> ListarAsync(CancellationToken cancellationToken)
    {
        return await dbContext.CategoriasHabito
            .AsNoTracking()
            .OrderBy(categoria => categoria.Orden)
            .Select(categoria => new CategoriaHabitoListado(categoria.Id, categoria.Nombre, categoria.Orden))
            .ToListAsync(cancellationToken);
    }
}
