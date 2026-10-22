using Constia.Application.Usuarios;
using Constia.Application.Correo;
using Constia.Infrastructure.Correo;
namespace Constia.API.Authentication;

public sealed class OpcionesVerificacionCorreo
{
    public bool PublicRegistrationEnabled { get; init; }
    public bool C2Enabled { get; init; }
    public string? PublicUrl { get; init; }
}
public sealed class ActivacionCorreo(OpcionesVerificacionCorreo opciones,
    PoliticaVerificacionCorreo politica, PoliticaProcesamientoCorreo procesamiento,
    ICanalVerificacionCorreo canal, OpcionesResend resend,
    IVerificacionCorreoRepository repository, IHostEnvironment environment) : IActivacionCorreo
{
    public async Task<bool> HabilitadoAsync(CancellationToken ct)
    {
        if (!opciones.C2Enabled || !opciones.PublicRegistrationEnabled || !politica.EsValida || !canal.Disponible ||
            !Uri.TryCreate(opciones.PublicUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) ||
            !string.IsNullOrEmpty(url.Fragment) || url.AbsolutePath != "/") return false;
        var pruebas = environment.IsEnvironment("Testing") && canal is ICanalCorreoDePrueba;
        if (!pruebas && (canal is ICanalCorreoDePrueba || !resend.Enabled ||
            ValidadorOpcionesResend.Validar(resend).Count != 0 ||
            !procesamiento.Compatible(TimeSpan.FromSeconds(resend.RequestTimeoutSeconds)))) return false;
        return await repository.EsquemaDisponibleAsync(ct);
    }
}
