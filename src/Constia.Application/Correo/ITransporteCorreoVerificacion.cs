namespace Constia.Application.Correo;

/// <summary>Un intento HTTP; no emite desafíos, consulta SQL ni coordina reintentos.</summary>
public interface ITransporteCorreoVerificacion
{
    Task<ResultadoEntregaCorreo> EnviarAsync(MensajeVerificacionCorreo mensaje, CancellationToken cancellationToken);
}
