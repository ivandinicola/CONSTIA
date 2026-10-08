namespace Constia.Application.Temporal;

public interface IFechaLocalUsuario
{
    DateOnly ObtenerHoy(string timeZoneId);
}
