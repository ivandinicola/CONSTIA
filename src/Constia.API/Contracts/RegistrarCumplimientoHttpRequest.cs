using System.ComponentModel.DataAnnotations;

namespace Constia.API.Contracts;

public sealed class RegistrarCumplimientoHttpRequest
{
    [Required]
    public DateOnly? Fecha { get; init; }
}
