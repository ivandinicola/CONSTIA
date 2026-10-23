using Constia.Application.Bienvenida;
using Constia.Application.Correo;
using Constia.Domain;
namespace Constia.Tests;
public sealed class ProcesarBienvenidaTests
{
    [Fact] public async Task AutorizacionAusente_NoRealizaHttpYLibera()
    {
        var r=new Repo();var t=new Transporte();
        var fin=await new ProcesarBienvenida(r,new Preparador(),t,TimeProvider.System).EjecutarAsync(default);
        Assert.Equal(0,t.Calls);Assert.True(r.Liberado);Assert.Equal(FinProcesamientoBienvenida.Pospuesto,fin);
    }
    [Fact] public async Task Resultado_SeRegistraSinNuevaEmisionNiReserva()
    {
        var r=new Repo{Autoriza=true};var t=new Transporte();
        await new ProcesarBienvenida(r,new Preparador(),t,TimeProvider.System).EjecutarAsync(default);
        Assert.Equal(1,t.Calls);Assert.Equal(1,r.Reservas);Assert.NotNull(r.Resultado);
    }
    [Fact] public async Task CommitReservaIncierto_NoEnvioNiLiberacionQueInventeCerteza()
    {
        var r=new Repo{FallaReserva=true};var t=new Transporte();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new ProcesarBienvenida(r,new Preparador(),t,TimeProvider.System).EjecutarAsync(default));
        Assert.Equal(0,t.Calls);Assert.False(r.Liberado);
    }

    [Fact]
    public async Task U_R1_ExcepcionRegistro_PropagaSinRepetirHttpNiReserva()
    {
        var fallo=new InvalidOperationException("Fallo simulado al registrar resultado.");
        var r=new Repo{Autoriza=true,ErrorRegistro=fallo};var t=new Transporte();
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new ProcesarBienvenida(r,new Preparador(),t,TimeProvider.System).EjecutarAsync(default));

        Assert.Same(fallo,ex);
        Assert.Equal(new[]{"adquirir","congelar","reservar","autorizar","registrar"},r.Pasos);
        Assert.Equal(1,t.Calls);Assert.Equal(1,r.Reservas);Assert.Equal(1,r.Registros);
        Assert.Equal(EstadoEntregaCorreo.AceptadaPorProveedor,r.Intentado!.Estado);
        Assert.Null(r.Resultado);Assert.False(r.Liberado);
    }

    [Fact]
    public async Task U_R2_RegistroRechazado_NoFuerzaAceptacionNiRepiteHttp()
    {
        var r=new Repo{Autoriza=true,RegistroAceptado=false};var t=new Transporte();
        var fin=await new ProcesarBienvenida(r,new Preparador(),t,TimeProvider.System).EjecutarAsync(default);

        // Finalizado describes the cycle, not durable provider acceptance.
        Assert.Equal(FinProcesamientoBienvenida.Finalizado,fin);
        Assert.Equal(new[]{"adquirir","congelar","reservar","autorizar","registrar"},r.Pasos);
        Assert.Equal(1,t.Calls);Assert.Equal(1,r.Reservas);Assert.Equal(1,r.Registros);
        Assert.Equal(EstadoEntregaCorreo.AceptadaPorProveedor,r.Intentado!.Estado);
        Assert.Null(r.Resultado);Assert.False(r.Liberado);
    }
    private sealed class Preparador : IPreparadorPayloadBienvenida
    {
        public string Perfil=>new string('a',64);
        public byte[] Preparar(EventoBienvenida e)=>[1];
    }
    private sealed class Transporte : ITransporteCorreoPreparado
    {
        public int Calls;
        public Task<ResultadoEntregaCorreo> EnviarAsync(SolicitudCorreoPreparada s,CancellationToken ct)
        {Calls++;return Task.FromResult(new ResultadoEntregaCorreo(EstadoEntregaCorreo.AceptadaPorProveedor,MotivoEntregaCorreo.Ninguno,"id",200,intentoHttpIniciado:true));}
    }
    private sealed class Repo : IOutboxBienvenidaRepository
    {
        public bool Autoriza,Liberado,FallaReserva; public int Reservas;public ResultadoEntregaCorreo? Resultado;
        public Exception? ErrorRegistro;
        public bool RegistroAceptado=true;
        public int Registros;
        public ResultadoEntregaCorreo? Intentado;
        public List<string> Pasos {get;}=[];
        private readonly LeaseBienvenida lease=new(Guid.NewGuid(),Guid.NewGuid(),1);
        public Task<TrabajoBienvenida?> AdquirirAsync(CancellationToken ct)
        {Pasos.Add("adquirir");var n=DateTimeOffset.UtcNow;return Task.FromResult<TrabajoBienvenida?>(new(lease,EventoBienvenida.CrearPendiente(Guid.NewGuid(),Guid.NewGuid(),"x@example.invalid",n,n)));}
        public Task<bool> CongelarAsync(LeaseBienvenida l,byte[] b,string p,CancellationToken ct){Pasos.Add("congelar");return Task.FromResult(true);}
        public Task<ReservaBienvenida?> ReservarAsync(LeaseBienvenida l,Guid i,CancellationToken ct)
        {Pasos.Add("reservar");Reservas++;if(FallaReserva)throw new InvalidOperationException("commit incierto");return Task.FromResult<ReservaBienvenida?>(new(l,i,1));}
        public Task<PermisoHttpBienvenida?> AutorizarHttpAsync(ReservaBienvenida r,CancellationToken ct)
        {Pasos.Add("autorizar");return Task.FromResult<PermisoHttpBienvenida?>(Autoriza?new(new("clave",[1]),TimeSpan.FromSeconds(50)):null);}
        public Task<bool> RegistrarResultadoAsync(ReservaBienvenida r,ResultadoEntregaCorreo x,CancellationToken ct)
        {
            Pasos.Add("registrar");Registros++;Intentado=x;
            if(ErrorRegistro is not null)throw ErrorRegistro;
            if(!RegistroAceptado)return Task.FromResult(false);
            Resultado=x;return Task.FromResult(true);
        }
        public Task LiberarAsync(LeaseBienvenida l,bool p,MotivoFinalBienvenida? f,CancellationToken ct){Pasos.Add("liberar");Liberado=true;return Task.CompletedTask;}
        public Task<int> RecuperarYVencerAsync(int m,CancellationToken ct)=>Task.FromResult(0);
        public Task PausarProveedorAsync(ResultadoEntregaCorreo r,CancellationToken ct)
        {Pasos.Add("pausar");return Task.CompletedTask;}
    }
}
