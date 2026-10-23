namespace Constia.Application.Correo;
public interface ITransporteCorreoPreparado
{
    Task<ResultadoEntregaCorreo> EnviarAsync(SolicitudCorreoPreparada solicitud, CancellationToken ct);
}
