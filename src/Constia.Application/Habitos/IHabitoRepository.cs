using Constia.Domain;

namespace Constia.Application.Habitos;

public interface IHabitoRepository
{
    Task AgregarAsync(Habito habito, CancellationToken cancellationToken);
}
