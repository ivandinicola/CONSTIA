using Constia.Domain;
using Microsoft.EntityFrameworkCore;

namespace Constia.API.Tests;

// Materializa escalares con EF y carga navegaciones mediante sus APIs, sin SQL ni setters públicos.
internal static class HabitoMetadataMaterialization
{
    internal static Habito Crear(Usuario usuario, Guid? categoriaId, string? iconoId)
    {
        using var context = CategoriaHabitoModelTests.CrearContexto();
        var source = HabitoHistorico.Crear(usuario, "Leer", "Historia", new DateOnly(2026, 10, 1), [DayOfWeek.Monday]);
        var values = context.Entry(source).CurrentValues.Clone();
        values[nameof(Habito.CategoriaId)] = categoriaId;
        values[nameof(Habito.IconoId)] = iconoId;
        values["UsuarioId"] = usuario.Id;
        values["ConcurrencyVersion"] = 7L;
        var materialized = (Habito)values.ToObject();
        var entry = context.Entry(materialized);
        entry.Reference(h => h.Usuario).CurrentValue = usuario;
        entry.Collection(h => h.Programaciones).CurrentValue = source.Programaciones.ToList();
        entry.Collection(h => h.PeriodosEstado).CurrentValue = source.PeriodosEstado.ToList();
        return materialized;
    }
}
