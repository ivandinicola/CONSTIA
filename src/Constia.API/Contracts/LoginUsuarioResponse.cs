namespace Constia.API.Contracts;

public sealed record LoginUsuarioResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string Name,
    string Email);
