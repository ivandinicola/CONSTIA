using System.ComponentModel.DataAnnotations;
using Constia.Application.Temporal;

namespace Constia.API.Contracts;

public sealed class RegistroUsuarioRequest
{
    [Required, StringLength(Constia.Application.Correo.DatosVerificacionCorreo.MaximoNombre)]
    public string Nombre { get; init; } = string.Empty;

    [Required, StringLength(Constia.Application.Correo.DatosVerificacionCorreo.MaximoEmail)]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    [Required]
    [StringLength(ZonaHorariaIana.LongitudMaximaIdentificador)]
    public string TimeZoneId { get; init; } = string.Empty;
}
