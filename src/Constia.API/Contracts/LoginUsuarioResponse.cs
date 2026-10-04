namespace Constia.API.Contracts;

public sealed record LoginUsuarioResponse(Guid UserId, string Name, string Email);
