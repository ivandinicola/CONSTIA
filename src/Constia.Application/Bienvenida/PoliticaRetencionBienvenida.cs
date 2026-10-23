using Constia.Domain;
namespace Constia.Application.Bienvenida;
public static class PoliticaRetencionBienvenida
{
    public static bool PuedeDepurar(EstadoEventoBienvenida estado, DateTimeOffset? finalizado,
        DateTimeOffset? ultimoLeaseHasta, DateTimeOffset ahora) =>
        estado is EstadoEventoBienvenida.AceptadoProveedor or EstadoEventoBienvenida.FinalSinAceptacion or EstadoEventoBienvenida.InciertoFinal
        && finalizado.HasValue && finalizado<=ahora
        && (!ultimoLeaseHasta.HasValue || ultimoLeaseHasta.Value.AddMinutes(10)<=ahora);
    public static bool PuedeEliminarDetalle(DateTimeOffset finalizado, DateTimeOffset ultimaReserva, DateTimeOffset ahora) =>
        finalizado.AddDays(30)<=ahora && ultimaReserva.AddHours(24)<=ahora;
}
