using System.Security.Claims;
using Constia.API.Authentication;
using Microsoft.AspNetCore.Http;

namespace Constia.API.Tests;

public class UsuarioActualTests
{
    [Fact]
    public void UsuarioId_ConUsuarioAutenticadoYSubValido_DevuelveGuid()
    {
        var id = Guid.NewGuid();
        var usuarioActual = CrearUsuarioActual(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], "test")));

        Assert.Equal(id, usuarioActual.UsuarioId);
    }

    [Fact]
    public void UsuarioId_SinUsuarioAutenticado_DevuelveNull()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]));
        var usuarioActual = CrearUsuarioActual(principal);

        Assert.Null(usuarioActual.UsuarioId);
    }

    [Fact]
    public void UsuarioId_SinClaimSub_DevuelveNull()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
        var usuarioActual = CrearUsuarioActual(principal);

        Assert.Null(usuarioActual.UsuarioId);
    }

    [Fact]
    public void UsuarioId_ConSubQueNoEsGuid_DevuelveNull()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "no-es-guid")], "test"));
        var usuarioActual = CrearUsuarioActual(principal);

        Assert.Null(usuarioActual.UsuarioId);
    }

    [Fact]
    public void UsuarioId_SinHttpContext_DevuelveNull()
    {
        var usuarioActual = new HttpContextUsuarioActual(new HttpContextAccessor());

        Assert.Null(usuarioActual.UsuarioId);
    }

    private static HttpContextUsuarioActual CrearUsuarioActual(ClaimsPrincipal principal)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        return new HttpContextUsuarioActual(accessor);
    }
}
