using Constia.Application.Correo;
using Constia.Domain;
namespace Constia.Application.Bienvenida;
public interface IOutboxBienvenidaRepository
{
    Task<TrabajoBienvenida?> AdquirirAsync(CancellationToken ct);
    Task<bool> CongelarAsync(LeaseBienvenida lease, byte[] payload, string perfil, CancellationToken ct);
    Task<ReservaBienvenida?> ReservarAsync(LeaseBienvenida lease, Guid intentoId, CancellationToken ct);
    Task<PermisoHttpBienvenida?> AutorizarHttpAsync(ReservaBienvenida reserva, CancellationToken ct);
    Task<bool> RegistrarResultadoAsync(ReservaBienvenida reserva, ResultadoEntregaCorreo resultado, CancellationToken ct);
    Task LiberarAsync(LeaseBienvenida lease, bool pausar, MotivoFinalBienvenida? final, CancellationToken ct);
    Task<int> RecuperarYVencerAsync(int maximo, CancellationToken ct);
    Task PausarProveedorAsync(ResultadoEntregaCorreo resultado, CancellationToken ct);
}
