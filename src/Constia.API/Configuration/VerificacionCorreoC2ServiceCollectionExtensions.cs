using Constia.API.Workers;
using Constia.Application.Correo;
using Constia.Infrastructure.Correo;
namespace Constia.API.Configuration;

public static class VerificacionCorreoC2ServiceCollectionExtensions
{
    public static IServiceCollection AddVerificacionCorreoC2(this IServiceCollection services)
    {
        services.AddSingleton<PoliticaProcesamientoCorreo>();
        services.AddSingleton<ICanalVerificacionCorreo, ColaVerificacionCorreoMemoria>();
        services.AddScoped<ProcesarCorreoVerificacion>();
        services.AddHostedService<ProcesadorVerificacionCorreo>();
        // El despliegue debe respetar también esta gracia; SIGKILL pierde la cola.
        services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(150));
        return services;
    }
}
