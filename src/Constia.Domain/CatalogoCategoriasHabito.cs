namespace Constia.Domain;

/// <summary>
/// Definición única del catálogo global aprobado. Los identificadores no se
/// generan ni se derivan del nombre o del orden de presentación.
/// </summary>
public static class CatalogoCategoriasHabito
{
    public static IReadOnlyList<CategoriaHabito> Todas { get; } =
        Array.AsReadOnly<CategoriaHabito>(
        [
            new(Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000001"), "Salud y movimiento", 1),
            new(Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000002"), "Alimentación e hidratación", 2),
            new(Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000003"), "Descanso y sueño", 3),
            new(Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000004"), "Bienestar emocional", 4),
            new(Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000005"), "Aprendizaje y crecimiento", 5),
            new(Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000006"), "Productividad y organización", 6),
            new(Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000007"), "Relaciones y vínculos", 7),
            new(Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000008"), "Finanzas personales", 8),
            new(Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000009"), "Creatividad y ocio", 9),
            new(Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000010"), "Hogar y vida cotidiana", 10)
        ]);

    public static bool EsOficial(Guid categoriaId) =>
        Todas.Any(categoria => categoria.Id == categoriaId);
}
