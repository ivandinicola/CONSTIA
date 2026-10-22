namespace Constia.Application.Correo;

public sealed class PoliticaProcesamientoCorreo
{
    public int Capacidad => 4;
    public int Intentos => 2;
    public TimeSpan Procesamiento => TimeSpan.FromSeconds(30);
    public TimeSpan Espera => TimeSpan.FromSeconds(120);
    public TimeSpan Reserva => TimeSpan.FromSeconds(15);
    public TimeSpan Sql => TimeSpan.FromSeconds(10);
    public TimeSpan Apagado => TimeSpan.FromSeconds(150);
    public bool Compatible(TimeSpan timeout) => timeout > TimeSpan.Zero && timeout <= TimeSpan.FromSeconds(10);
    public TimeSpan? EsperaReintento(ResultadoEntregaCorreo resultado, TimeSpan restante, TimeSpan timeout)
    {
        if (!PoliticaReintentoCorreo.EsCandidatoMismaSolicitud(resultado)) return null;
        var espera = resultado.EsperaMinima is { } value && value > TimeSpan.FromSeconds(1)
            ? value : TimeSpan.FromSeconds(1);
        return espera + timeout < restante ? espera : null;
    }
}
