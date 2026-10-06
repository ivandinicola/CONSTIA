using Microsoft.AspNetCore.Http;

namespace Constia.API.Authentication;

public sealed class HttpContextUsuarioActual(IHttpContextAccessor httpContextAccessor) : IUsuarioActual
{
    public Guid? UsuarioId
    {
        get
        {
            var usuario = httpContextAccessor.HttpContext?.User;
            if (usuario?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var sub = usuario.FindFirst("sub")?.Value;
            return Guid.TryParse(sub, out var usuarioId) ? usuarioId : null;
        }
    }
}
