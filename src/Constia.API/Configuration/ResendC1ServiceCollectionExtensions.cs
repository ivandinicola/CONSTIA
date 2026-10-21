using Constia.Application.Correo;
using Constia.Infrastructure.Correo;
namespace Constia.API.Configuration;

public static class ResendC1ServiceCollectionExtensions
{
    public const string NombreCliente = "CONSTIA.Resend.C1";

    public static IServiceCollection AddResendC1(this IServiceCollection services, IConfiguration configuration)
    {
        // Lectura tolerante: configuración inválida cierra el cliente, no derriba login/confirmación.
        var enabled = bool.TryParse(configuration["Resend:Enabled"], out var value) && value;
        var rawTimeout = configuration["Resend:RequestTimeoutSeconds"];
        var timeout = rawTimeout is null ? 10 : int.TryParse(rawTimeout, out var number) ? number : 0;
        var opciones = new OpcionesResend
        {
            Enabled = enabled, ApiKey = configuration["Resend:ApiKey"],
            FromAddress = configuration["Resend:FromAddress"],
            FromName = configuration["Resend:FromName"] ?? "CONSTIA",
            PublicOrigin = configuration["VerificacionCorreo:PublicUrl"],
            RequestTimeoutSeconds = timeout
        };
        services.AddSingleton(opciones);
        services.AddTransient<IPreparadorMensajeVerificacion, PreparadorMensajeVerificacion>();
        services.AddHttpClient(NombreCliente, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseCookies = false
                // TLS normal del sistema. No callback que acepte certificados inválidos.
            })
            .RemoveAllLoggers()
            .AddTypedClient<ITransporteCorreoVerificacion, ResendEmailClient>();
        // Deliberadamente sin registros IEntregaCorreoVerificacion/IEntregaCorreoProduccion de Etapa B.
        return services;
    }
}
