namespace Constia.Domain;

public class Cumplimiento
{
    public Guid Id { get; private set; }

    public Habito Habito { get; private set; }

    public DateOnly Fecha { get; private set; }

    private Cumplimiento()
    {
        Habito = null!;
    }

    public Cumplimiento(Habito habito, DateOnly fecha)
    {
        ArgumentNullException.ThrowIfNull(habito);

        Id = Guid.NewGuid();
        Habito = habito;
        Fecha = fecha;
    }
}
