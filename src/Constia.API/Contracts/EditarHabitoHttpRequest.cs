using System.ComponentModel.DataAnnotations;

namespace Constia.API.Contracts;

public sealed class EditarHabitoHttpRequest
{
    [Required]
    public string Nombre { get; init; } = string.Empty;

    public string? Descripcion { get; init; }

    [Required]
    public IReadOnlyCollection<DayOfWeek>? DiasProgramados { get; init; }
}
