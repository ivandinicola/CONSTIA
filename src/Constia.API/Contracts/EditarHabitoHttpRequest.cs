using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Constia.API.Contracts;

public sealed class EditarHabitoHttpRequest : IValidatableObject
{
    private Guid? _categoriaId;
    private string? _iconoId;
    private int _categoriaAsignaciones;
    private int _iconoAsignaciones;

    [Required]
    public string Nombre { get; init; } = string.Empty;

    public string? Descripcion { get; init; }

    [Required]
    public IReadOnlyCollection<DayOfWeek>? DiasProgramados { get; init; }

    public Guid? CategoriaId
    {
        get => _categoriaId;
        init { _categoriaId = value; _categoriaAsignaciones++; }
    }

    public string? IconoId
    {
        get => _iconoId;
        init { _iconoId = value; _iconoAsignaciones++; }
    }

    [JsonIgnore]
    public bool CategoriaIdInformada => _categoriaAsignaciones > 0;

    [JsonIgnore]
    public bool IconoIdInformado => _iconoAsignaciones > 0;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (_categoriaAsignaciones > 1)
            yield return new ValidationResult("No se permite repetir categoriaId.", ["categoriaId"]);
        else if (CategoriaIdInformada && (CategoriaId is null || CategoriaId == Guid.Empty))
            yield return new ValidationResult("La categoría informada no puede ser null ni vacía.", ["categoriaId"]);

        if (_iconoAsignaciones > 1)
            yield return new ValidationResult("No se permite repetir iconoId.", ["iconoId"]);
        else if (IconoIdInformado && string.IsNullOrWhiteSpace(IconoId))
            yield return new ValidationResult("El ícono informado no puede ser null ni vacío.", ["iconoId"]);
    }
}
