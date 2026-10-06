using Constia.Application.Habitos;
using Constia.Domain;

namespace Constia.Infrastructure.Habitos;

public sealed class HabitoRepository(ConstiaDbContext dbContext) : IHabitoRepository
{
    public async Task AgregarAsync(Habito habito, CancellationToken cancellationToken)
    {
        dbContext.Set<Habito>().Add(habito);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
