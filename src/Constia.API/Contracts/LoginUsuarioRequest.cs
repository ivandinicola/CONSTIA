using System.ComponentModel.DataAnnotations;

namespace Constia.API.Contracts;

public sealed class LoginUsuarioRequest
{
    [Required]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}
