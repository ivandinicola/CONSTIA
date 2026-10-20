namespace Constia.Domain;

/// <summary>Definición única del catálogo fijo de íconos de hábitos aprobados para V1.</summary>
public static class CatalogoIconosHabito
{
    public static IReadOnlyList<IconoHabito> Todos { get; } =
        Array.AsReadOnly<IconoHabito>(
        [
            new("libro", "Libro", 1),
            new("fuerza", "Fuerza", 2),
            new("agua", "Agua", 3),
            new("loto", "Loto", 4),
            new("manzana", "Manzana", 5),
            new("luna", "Luna", 6),
            new("cafe", "Café", 7),
            new("corredor", "Corredor", 8),
            new("corazon", "Corazón", 9),
            new("cerebro", "Cerebro", 10),
            new("sol", "Sol", 11),
            new("huella", "Huella", 12),
            new("brote", "Brote", 13),
            new("portatil", "Portátil", 14),
            new("musica", "Música", 15),
            new("billete", "Billete", 16)
        ]);

    public static bool EsOficial(string? iconoId) =>
        Todos.Any(icono => string.Equals(icono.Id, iconoId, StringComparison.Ordinal));
}
