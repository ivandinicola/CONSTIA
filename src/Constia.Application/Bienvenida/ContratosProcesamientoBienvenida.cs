using Constia.Application.Correo;
using Constia.Domain;
namespace Constia.Application.Bienvenida;
public sealed record LeaseBienvenida(Guid EventoId, Guid Propietario, long Generacion)
{
    public override string ToString() => "[lease redactado]";
}
public sealed record TrabajoBienvenida(LeaseBienvenida Lease, EventoBienvenida Evento)
{
    public override string ToString() => "[trabajo redactado]";
}
public sealed record ReservaBienvenida(LeaseBienvenida Lease, Guid IntentoId, int Ordinal)
{
    public override string ToString() => "[reserva redactada]";
}
public sealed record PermisoHttpBienvenida(SolicitudCorreoPreparada Solicitud, TimeSpan Vigencia)
{
    public override string ToString() => "[permiso redactado]";
}
public enum FinProcesamientoBienvenida { SinTrabajo, Finalizado, Pospuesto, NoDisponible }
