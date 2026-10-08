using System.ComponentModel.DataAnnotations;
using Constia.Application.Temporal;

namespace Constia.API.Contracts;

public sealed class CambiarZonaHorariaRequest
{
    [Required]
    [StringLength(ZonaHorariaIana.LongitudMaximaIdentificador)]
    public string TimeZoneId { get; init; } = string.Empty;
}
