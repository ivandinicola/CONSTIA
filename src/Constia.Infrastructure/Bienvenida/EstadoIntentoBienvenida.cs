namespace Constia.Infrastructure.Bienvenida;
public enum EstadoIntentoBienvenida : byte
{
    ReservadoSinResultado=1, AceptadoProveedor=2, RechazoConocido=3,
    TemporalConocido=4, NoIniciadoConfirmado=5, Incierto=6
}
