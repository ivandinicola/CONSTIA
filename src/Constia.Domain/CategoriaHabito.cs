namespace Constia.Domain;

public sealed class CategoriaHabito
{
    public Guid Id { get; private set; }

    public string Nombre { get; private set; }

    public int Orden { get; private set; }

    public CategoriaHabito(Guid id, string nombre, int orden)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("La categoría debe tener un identificador válido.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orden);

        Id = id;
        Nombre = nombre;
        Orden = orden;
    }
}
