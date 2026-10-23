using Constia.Application.Bienvenida;
using Constia.Infrastructure.Correo;
namespace Constia.API.Workers;
public sealed class ProcesadorBienvenida(IServiceScopeFactory scopes,OpcionesProcesamientoBienvenida opciones,
    OpcionesResend resend,TimeProvider clock,ILogger<ProcesadorBienvenida> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!opciones.EsValida){logger.LogError("Procesamiento de bienvenida desactivado: configuración inválida.");return;}
        var ultima=clock.GetTimestamp();var primerMantenimiento=true;
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if(opciones.MantenimientoHabilitado && (primerMantenimiento ||
                    clock.GetElapsedTime(ultima)>=PoliticaProcesamientoBienvenida.Mantenimiento))
                {
                    // Consume the interval even if retention fails after recovery committed.
                    primerMantenimiento=false;ultima=clock.GetTimestamp();
                    await using var scope=scopes.CreateAsyncScope();
                    var repo=scope.ServiceProvider.GetRequiredService<IOutboxBienvenidaRepository>();
                    var usados=await repo.RecuperarYVencerAsync(100,stoppingToken);
                    if(usados<100)await scope.ServiceProvider.GetRequiredService<IRetencionBienvenidaRepository>()
                        .AplicarAsync(100-usados,stoppingToken);
                }
                if(opciones.ProcesamientoHabilitado && opciones.EnvioHabilitado && resend.Enabled &&
                    ValidadorOpcionesResend.ValidarTransporte(resend).Count==0)
                {
                    await using var scope=scopes.CreateAsyncScope();
                    var fin=await scope.ServiceProvider.GetRequiredService<ProcesarBienvenida>().EjecutarAsync(stoppingToken);
                    logger.LogInformation("Procesamiento de bienvenida: {Resultado}",fin);
                }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){logger.LogError("Ciclo de bienvenida no disponible.");}
            try{await Task.Delay(PoliticaProcesamientoBienvenida.Polling,clock,stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
        }
    }
}
