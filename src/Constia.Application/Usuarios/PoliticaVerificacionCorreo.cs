namespace Constia.Application.Usuarios;

public sealed class PoliticaVerificacionCorreo
{
    public int SolicitudesPorIp { get; init; } = 10;
    public int VentanaSolicitudesMinutos { get; init; } = 10;
    public int EmisionesPorUsuario { get; init; } = 5;
    public int IntervaloEmisionesSegundos { get; init; } = 60;
    public int EmisionesGlobales { get; init; } = 40;
    // Mismo umbral aprobado de solicitudes, aplicado además por desafío contra ataques distribuidos.
    public int IntentosPorDesafio => SolicitudesPorIp;
    public TimeSpan VentanaIntentos => TimeSpan.FromMinutes(VentanaSolicitudesMinutos);
    public bool EsValida => SolicitudesPorIp > 0 && SolicitudesPorIp <= 1000 &&
        VentanaSolicitudesMinutos > 0 && VentanaSolicitudesMinutos <= 1440 &&
        EmisionesPorUsuario > 0 && EmisionesPorUsuario <= 100 &&
        IntervaloEmisionesSegundos >= 60 && IntervaloEmisionesSegundos <= 86400 &&
        EmisionesGlobales > 0 && EmisionesGlobales <= 100000;
    public bool PermiteEmision(IReadOnlyCollection<DateTimeOffset> emisiones, DateTimeOffset ahora) =>
        EsValida && emisiones.Count(x => x > ahora.AddHours(-24)) < EmisionesPorUsuario &&
        (emisiones.Count == 0 || ahora >= emisiones.Max().AddSeconds(IntervaloEmisionesSegundos));
}
