using Constia.Application.Usuarios;
namespace Constia.API.Authentication;

public sealed class OpcionesVerificacionCorreo
{
    public bool PublicRegistrationEnabled { get; init; }
    public string? PublicUrl { get; init; }
}

public sealed class ActivacionCorreo(
    OpcionesVerificacionCorreo opciones, PoliticaVerificacionCorreo politica,
    IEntregaCorreoVerificacion entrega, IVerificacionCorreoRepository repository,
    IHostEnvironment environment) : IActivacionCorreo
{
    public async Task<bool> HabilitadoAsync(CancellationToken ct)
    {
        if (!opciones.PublicRegistrationEnabled || !politica.EsValida ||
            !Uri.TryCreate(opciones.PublicUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
            return false;
        var pruebas = environment.IsEnvironment("Testing") && entrega is IEntregaCorreoDePrueba;
        var produccion = entrega is IEntregaCorreoProduccion { Operativo: true } && entrega is not IEntregaCorreoDePrueba;
        if (!pruebas && !produccion) return false;
        return await repository.EsquemaDisponibleAsync(ct);
    }
}

// Puerto cerrado: Etapa B no contiene adaptador de red, proveedor no-op operativo ni tokens en logs.
public sealed class EntregaCorreoNoDisponible : IEntregaCorreoVerificacion
{
    public Task<bool> IntentarEntregarAsync(EmisionCorreo emision, string token, CancellationToken ct) =>
        Task.FromResult(false);
}
