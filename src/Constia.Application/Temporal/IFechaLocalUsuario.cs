namespace Constia.Application.Temporal;

public interface IFechaLocalUsuario
{
    DateOnly ObtenerHoy(string timeZoneId);

    DateOnly ObtenerFechaLocal(DateTimeOffset instanteUtc, string timeZoneId);
}
