namespace Constia.Application.Bienvenida;
public interface IRetencionBienvenidaRepository
{
    Task<int> AplicarAsync(int maximo, CancellationToken ct);
}
