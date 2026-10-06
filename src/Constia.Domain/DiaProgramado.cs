namespace Constia.Domain;

public class DiaProgramado
{
    public DayOfWeek Dia { get; private set; }

    private DiaProgramado()
    {
    }

    internal DiaProgramado(DayOfWeek dia)
    {
        Dia = dia;
    }
}
