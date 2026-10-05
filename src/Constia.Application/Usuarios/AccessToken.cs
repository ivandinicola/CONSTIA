namespace Constia.Application.Usuarios;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
