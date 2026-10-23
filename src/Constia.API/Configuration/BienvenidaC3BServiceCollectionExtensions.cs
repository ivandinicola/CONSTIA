using Constia.API.Workers;
using Constia.Application.Bienvenida;
using Constia.Application.Correo;
using Constia.Infrastructure.Bienvenida;
using Constia.Infrastructure.Correo;
namespace Constia.API.Configuration;
public static class BienvenidaC3BServiceCollectionExtensions
{
    public const string NombreCliente="CONSTIA.Resend.C3";
    public static IServiceCollection AddBienvenidaC3B(this IServiceCollection services,IConfiguration configuration)
    {
        services.AddSingleton(sp=>{
            var c=sp.GetRequiredService<IConfiguration>();
            return OpcionesProcesamientoBienvenida.DesdeValores(c["Bienvenida:ProcesamientoHabilitado"],
                c["Bienvenida:EnvioHabilitado"],c["Bienvenida:MantenimientoHabilitado"],
                c["Bienvenida:CuentaProveedor"],c["Resend:FromAddress"]);
        });
        services.AddScoped<IOutboxBienvenidaRepository,OutboxBienvenidaRepository>();
        services.AddScoped<IPreparadorPayloadBienvenida>(sp=>new PreparadorPayloadBienvenida(
            sp.GetRequiredService<OpcionesProcesamientoBienvenida>().Perfil ??
                throw new InvalidOperationException("Perfil no disponible.")));
        services.AddScoped<ProcesarBienvenida>();
        services.AddHttpClient(NombreCliente,c=>c.Timeout=Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(()=>new SocketsHttpHandler{AllowAutoRedirect=false,UseCookies=false})
            .RemoveAllLoggers()
            .AddTypedClient<ITransporteCorreoPreparado>((http,sp)=>{
                var c=sp.GetRequiredService<OpcionesResend>();
                // A separate immutable snapshot enforces C3's 10s without changing C1/C2 options.
                return new ResendHttpTransport(http,new OpcionesResend{Enabled=c.Enabled,ApiKey=c.ApiKey,
                    FromAddress=c.FromAddress,FromName=c.FromName,RequestTimeoutSeconds=10},
                    sp.GetRequiredService<TimeProvider>());
            });
        if(bool.TryParse(configuration["Bienvenida:ProcesamientoHabilitado"],out var p)&&p ||
            bool.TryParse(configuration["Bienvenida:MantenimientoHabilitado"],out var m)&&m)
            services.AddHostedService<ProcesadorBienvenida>();
        return services;
    }
}
