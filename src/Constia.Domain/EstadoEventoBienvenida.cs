namespace Constia.Domain;
public enum EstadoEventoBienvenida : byte
{
    Pendiente=1, Tomado=2, ReservadoSinResultado=3, EsperandoReintento=4,
    Pausado=5, AceptadoProveedor=6, FinalSinAceptacion=7, InciertoFinal=8
}
public enum MotivoFinalBienvenida : byte
{
    Ninguno=0, Rechazo=1, Agotado=2, Vencido=3, DestinatarioNoElegible=4,
    PreparacionInvalida=5, MargenInsuficiente=6, IncertidumbreNoReintentable=7
}
