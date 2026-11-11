namespace Constia.Web.Habitos;

public static class PresentacionHabitos
{
    public static readonly (DayOfWeek Dia, string Corto, string Nombre)[] Semana =
    [ (DayOfWeek.Monday,"Lun","lunes"), (DayOfWeek.Tuesday,"Mar","martes"), (DayOfWeek.Wednesday,"Mié","miércoles"),
      (DayOfWeek.Thursday,"Jue","jueves"), (DayOfWeek.Friday,"Vie","viernes"), (DayOfWeek.Saturday,"Sáb","sábado"), (DayOfWeek.Sunday,"Dom","domingo") ];
    public static string Resumen(IEnumerable<DayOfWeek> dias) => string.Join(" · ", Semana.Where(d => dias.Contains(d.Dia)).Select(d => d.Corto));
    public static string? Icono(string? id) => id switch
    {
        "libro" => "constia-lectura.png", "fuerza" => "constia-movimiento.png", "agua" => "constia-agua.png",
        "loto" => "constia-meditacion.png", "manzana" => "constia-alimentacion.png", "musica" => "constia-nota-musical.png",
        "estiramientos" => "constia-estiramientos.png",
        "luna" or "cafe" or "corredor" or "corazon" or "cerebro" or "sol" or "huella" or "brote" or "portatil" or "billete" => $"constia-{id}.png",
        _ => null
    };
}
