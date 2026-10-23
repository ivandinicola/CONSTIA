using Constia.Application.Bienvenida;
using Constia.Infrastructure.Bienvenida;
namespace Constia.API.Configuration;
public static class BienvenidaC3AServiceCollectionExtensions
{
    public static IServiceCollection AddBienvenidaC3A(this IServiceCollection services,IConfiguration configuration)
    {
        services.AddSingleton(sp =>
        {
            var actual = sp.GetRequiredService<IConfiguration>();
            return OpcionesCapturaBienvenida.DesdeValores(
                actual["Bienvenida:CapturarEventos"], actual["Bienvenida:EpocaCaptura"]);
        });
        services.AddScoped<IRetencionBienvenidaRepository,RetencionBienvenidaRepository>();
        // Sin worker, transporte, scheduler ni activación.
        return services;
    }
}
