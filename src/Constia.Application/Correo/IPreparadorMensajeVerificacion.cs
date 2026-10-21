namespace Constia.Application.Correo;

public interface IPreparadorMensajeVerificacion
{
    MensajeVerificacionCorreo Preparar(Guid desafioId, string destinatario, string nombre, string token,
        DateTimeOffset emitidoEnUtc, DateTimeOffset expiraEnUtc);
}
