using System.ComponentModel.DataAnnotations;
namespace Constia.API.Contracts;

/// <summary>Confirmación explícita del desafío con la contraseña existente; no aplica la política de altas.</summary>
public sealed record ConfirmarCorreoRequest
{
    public Guid DesafioId { get; init; }
    [Required] public string Token { get; init; } = string.Empty;
    [Required] public string Password { get; init; } = string.Empty;
    public override string ToString() => "[confirmación de correo redactada]";
}

/// <summary>Solicitud de reenvío con respuesta pública independiente de la existencia de la cuenta.</summary>
public sealed record ReenviarCorreoRequest
{
    [Required, StringLength(254)] public string Email { get; init; } = string.Empty;
    public override string ToString() => "[reenvío de correo redactado]";
}
