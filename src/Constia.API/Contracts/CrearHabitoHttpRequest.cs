using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Constia.API.Contracts;

public sealed class CrearHabitoHttpRequest : IValidatableObject
{
    [Required]
    public string Nombre { get; init; } = string.Empty;

    public string? Descripcion { get; init; }

    [JsonPropertyName("categoriaId")]
    public Guid? CategoriaId { get; init; }

    [JsonPropertyName("iconoId")]
    public string? IconoId { get; init; }

    [Required]
    public IReadOnlyCollection<DayOfWeek>? DiasProgramados { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? PropiedadesAdicionales { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PropiedadesAdicionales?.Keys.Any(key =>
                key.Equals("fechaInicio", StringComparison.OrdinalIgnoreCase)) == true)
            yield return new ValidationResult(
                "La fecha de inicio es calculada por el servidor.", ["fechaInicio"]);
        if (string.IsNullOrWhiteSpace(IconoId))
            yield return new ValidationResult("El ícono es obligatorio.", ["iconoId"]);
        if (CategoriaId is null || CategoriaId == Guid.Empty)
            yield return new ValidationResult("La categoría debe ser oficial.", ["categoriaId"]);
    }
}
