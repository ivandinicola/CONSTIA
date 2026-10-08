using System.ComponentModel.DataAnnotations;
using Constia.Application.Temporal;

namespace Constia.API.Contracts;

public sealed class RegistroUsuarioRequest
{
    [Required]
    public string Nombre { get; init; } = string.Empty;

    [Required]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    [Required]
    [StringLength(ZonaHorariaIana.LongitudMaximaIdentificador)]
    public string TimeZoneId { get; init; } = string.Empty;
}
