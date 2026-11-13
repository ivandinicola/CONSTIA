namespace Constia.Web.Habitos;

public static class PresentacionHabitos
{
    public static readonly (DayOfWeek Dia, string Corto, string Nombre)[] Semana =
    [ (DayOfWeek.Monday,"Lun","lunes"), (DayOfWeek.Tuesday,"Mar","martes"), (DayOfWeek.Wednesday,"Mié","miércoles"),
      (DayOfWeek.Thursday,"Jue","jueves"), (DayOfWeek.Friday,"Vie","viernes"), (DayOfWeek.Saturday,"Sáb","sábado"), (DayOfWeek.Sunday,"Dom","domingo") ];
    public static string Resumen(IEnumerable<DayOfWeek> dias) => string.Join(" · ", Semana.Where(d => dias.Contains(d.Dia)).Select(d => d.Corto));
    private static readonly (string Id, string Archivo)[] Iconos =
    [
        ("libro", "constia-lectura.png"), ("fuerza", "constia-movimiento.png"),
        ("agua", "constia-agua.png"), ("loto", "constia-meditacion.png"),
        ("manzana", "constia-alimentacion.png"), ("luna", "constia-luna.png"),
        ("cafe", "constia-cafe.png"), ("corredor", "constia-corredor.png"),
        ("corazon", "constia-corazon.png"), ("cerebro", "constia-cerebro.png"),
        ("sol", "constia-sol.png"), ("huella", "constia-huella.png"),
        ("brote", "constia-brote.png"), ("portatil", "constia-portatil.png"),
        ("musica", "constia-nota-musical.png"), ("billete", "constia-billete.png"),
        ("estiramientos", "constia-estiramientos.png"), ("higiene-bucal", "constia-higiene-bucal.png"),
        ("medicacion", "constia-medicacion.png"), ("diario", "constia-diario.png"),
        ("hogar", "constia-hogar.png"), ("planificacion", "constia-planificacion.png"),
        ("cuidado-personal", "constia-cuidado-personal.png"), ("caminata", "constia-caminata.png"),
        ("idiomas", "constia-idiomas.png"), ("desconexion-digital", "constia-desconexion-digital.png"),
        ("vinculos", "constia-vinculos.png"), ("bicicleta", "constia-bicicleta.png")
    ];

    public static string? Icono(string? id) => Array.Find(Iconos, icono => icono.Id == id).Archivo;

    internal static IconoAlta[]? OrdenarCatalogoIconos(IconoAlta[]? opciones)
    {
        if (opciones is not { Length: 16 or 28 } ||
            opciones.Any(icono => icono is null || string.IsNullOrWhiteSpace(icono.Nombre)))
            return null;

        var ordenadas = opciones.OrderBy(icono => icono.Orden).ToArray();
        for (var i = 0; i < ordenadas.Length; i++)
        {
            if (ordenadas[i].Orden != i + 1 || ordenadas[i].Id != Iconos[i].Id)
                return null;
        }

        return ordenadas;
    }
}
