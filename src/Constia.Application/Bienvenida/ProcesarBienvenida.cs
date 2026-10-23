using Constia.Application.Correo;
using Constia.Domain;
namespace Constia.Application.Bienvenida;
public sealed class ProcesarBienvenida(IOutboxBienvenidaRepository repo, IPreparadorPayloadBienvenida preparador,
    ITransporteCorreoPreparado transporte, TimeProvider clock)
{
    public async Task<FinProcesamientoBienvenida> EjecutarAsync(CancellationToken ct)
    {
        var trabajo=await repo.AdquirirAsync(ct);
        if(trabajo is null)return FinProcesamientoBienvenida.SinTrabajo;
        var e=trabajo.Evento;
        if(e.PayloadUtf8 is not null && e.PerfilProveedorVersion!=preparador.Perfil)
        {
            await repo.LiberarAsync(trabajo.Lease,true,null,ct);return FinProcesamientoBienvenida.Pospuesto;
        }
        if(e.PayloadUtf8 is null)
        {
            byte[] payload;
            try{payload=preparador.Preparar(e);}
            catch(ArgumentException)
            {
                await repo.LiberarAsync(trabajo.Lease,false,MotivoFinalBienvenida.DestinatarioNoElegible,ct);
                return FinProcesamientoBienvenida.Finalizado;
            }
            if(!await repo.CongelarAsync(trabajo.Lease,payload,preparador.Perfil,ct))
                return FinProcesamientoBienvenida.Pospuesto;
        }
        // A failed/uncertain reservation is never followed by HTTP or a fabricated no-start result.
        var reserva=await repo.ReservarAsync(trabajo.Lease,Guid.NewGuid(),ct);
        if(reserva is null)return FinProcesamientoBienvenida.Pospuesto;
        var inicio=clock.GetTimestamp();
        var permiso=await repo.AutorizarHttpAsync(reserva,ct);
        if(permiso is null || permiso.Vigencia-clock.GetElapsedTime(inicio)<=PoliticaProcesamientoBienvenida.Http)
        {
            await repo.LiberarAsync(trabajo.Lease,false,null,ct);return FinProcesamientoBienvenida.Pospuesto;
        }
        ResultadoEntregaCorreo resultado;
        using var plazo=new CancellationTokenSource(PoliticaProcesamientoBienvenida.Http,clock);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,plazo.Token);
        if(linked.IsCancellationRequested)
            resultado=new(EstadoEntregaCorreo.CanceladaAntesDelEnvio,MotivoEntregaCorreo.Cancelacion);
        else
        {
            try{resultado=await transporte.EnviarAsync(permiso.Solicitud,linked.Token);}
            catch(OperationCanceledException){resultado=new(EstadoEntregaCorreo.ResultadoIncierto,MotivoEntregaCorreo.Timeout,intentoHttpIniciado:true);}
            catch(Exception){resultado=new(EstadoEntregaCorreo.ResultadoIncierto,MotivoEntregaCorreo.Red,intentoHttpIniciado:true);}
        }
        // Fresh bounded token: shutdown must not erase an already known result.
        using var persistir=new CancellationTokenSource(TimeSpan.FromSeconds(5),clock);
        await repo.RegistrarResultadoAsync(reserva,resultado,persistir.Token);
        return FinProcesamientoBienvenida.Finalizado;
    }
}
