namespace Constia.Domain;

public sealed class IconoHabito(string id, string nombre, int orden)
{
    public string Id { get; } = id;

    public string Nombre { get; } = nombre;

    public int Orden { get; } = orden;
}
