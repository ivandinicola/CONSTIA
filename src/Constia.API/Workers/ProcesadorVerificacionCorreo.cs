using Constia.Application.Correo;
namespace Constia.API.Workers;

public sealed class ProcesadorVerificacionCorreo(ICanalVerificacionCorreo canal,
    IServiceScopeFactory scopes, ILogger<ProcesadorVerificacionCorreo> logger,
    PoliticaProcesamientoCorreo politica, TimeProvider clock) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        canal.Iniciar();
        return base.StartAsync(cancellationToken);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        canal.Iniciar();
        try
        {
            while (await canal.LeerAsync(stoppingToken) is { } trabajo)
            {
                var fin = FinTrabajoCorreo.FalloInfraestructura;
                try
                {
                    var inicio = clock.GetTimestamp();
                    using var presupuesto = new CancellationTokenSource(politica.Procesamiento, clock);
                    // Al agotar el presupuesto global se cierra tanto si la operación coopera
                    // como si continúa bloqueada. La plaza sigue activa hasta su finalización.
                    using var falloPorPlazo = presupuesto.Token.Register(canal.Fallar);
                    using var ejecucion = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, presupuesto.Token);
                    await using var scope = scopes.CreateAsyncScope();
                    fin = await scope.ServiceProvider.GetRequiredService<ProcesarCorreoVerificacion>()
                        .EjecutarAsync(trabajo, ejecucion.Token);
                    // También comprobar el reloj: la cancelación cooperativa del coordinador
                    // puede finalizar antes de que se ejecute el callback de este temporizador.
                    if (clock.GetElapsedTime(inicio) >= politica.Procesamiento || presupuesto.IsCancellationRequested ||
                        fin is FinTrabajoCorreo.FalloInfraestructura or FinTrabajoCorreo.PreparacionFallida or
                            FinTrabajoCorreo.ProveedorNoDisponible or FinTrabajoCorreo.ProveedorNoDisponibleIncierto)
                        canal.Fallar();
                }
                finally
                {
                    trabajo.Finalizar(fin);
                    // Sólo enum; nunca trabajo, excepción, destinatario ni mensaje.
                    logger.LogInformation("Finalización de correo: {Resultado}", fin);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception)
        {
            canal.Fallar();
            logger.LogError("Trabajador de correo no disponible.");
        }
        finally { canal.Fallar(); }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        canal.CerrarAdmision();
        try { await canal.EsperarVacioAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        await base.StopAsync(cancellationToken);
    }
}
