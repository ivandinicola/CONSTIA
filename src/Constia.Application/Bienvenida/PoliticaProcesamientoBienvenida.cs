using Constia.Application.Correo;
using Constia.Domain;
namespace Constia.Application.Bienvenida;
public static class PoliticaProcesamientoBienvenida
{
    public const int MaxReservas=3, Presupuesto=20, Lote=100;
    public static readonly TimeSpan Lease=TimeSpan.FromSeconds(90), Http=TimeSpan.FromSeconds(10),
        Polling=TimeSpan.FromSeconds(5), Mantenimiento=TimeSpan.FromMinutes(1);
    public static EstadoEventoBienvenida Final(bool incierto) =>
        incierto ? EstadoEventoBienvenida.InciertoFinal : EstadoEventoBienvenida.FinalSinAceptacion;
    public static TimeSpan Espera(int reservas, TimeSpan? retryAfter)
    {
        var min=TimeSpan.FromMinutes(reservas==1?1:5);
        return retryAfter is { } r && r>min?r:min;
    }
    public static bool EsPausa(ResultadoEntregaCorreo r) => r.Estado==EstadoEntregaCorreo.ProveedorNoDisponible &&
        r.Motivo is MotivoEntregaCorreo.Autorizacion or MotivoEntregaCorreo.Tls or MotivoEntregaCorreo.CuotaDiaria or MotivoEntregaCorreo.CuotaMensual;
    public static int MotivoPausa(ResultadoEntregaCorreo r) => r.Motivo switch {
        MotivoEntregaCorreo.Autorizacion=>1, MotivoEntregaCorreo.Tls=>2,
        MotivoEntregaCorreo.CuotaDiaria=>3, MotivoEntregaCorreo.CuotaMensual=>4, _=>throw new ArgumentException("No es pausa de proveedor.") };
}
