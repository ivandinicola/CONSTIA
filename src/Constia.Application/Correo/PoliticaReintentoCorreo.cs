namespace Constia.Application.Correo;

/// <summary>Elegibilidad, no autorización ni ejecución. C2 coordina plazos e incertidumbre acumulada.</summary>
public static class PoliticaReintentoCorreo
{
    public static bool EsCandidatoMismaSolicitud(ResultadoEntregaCorreo resultado) =>
        resultado.Estado is EstadoEntregaCorreo.FalloTemporal or EstadoEntregaCorreo.ResultadoIncierto &&
        resultado.Motivo is not (MotivoEntregaCorreo.RespuestaDesconocida or
            MotivoEntregaCorreo.RespuestaMalformada or MotivoEntregaCorreo.RespuestaDemasiadoGrande);
}
