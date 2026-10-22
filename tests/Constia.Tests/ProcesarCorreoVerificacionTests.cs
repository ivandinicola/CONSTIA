using Constia.Application.Correo;
using Constia.Application.Usuarios;
using Constia.Domain;
namespace Constia.Tests;

public sealed class ProcesarCorreoVerificacionTests
{
    private static MensajeVerificacionCorreo Mensaje() => new(Guid.NewGuid(),"a@example.invalid",
        "correo@example.invalid","CONSTIA","Confirmar","<p>sintético</p>","sintético",
        DateTimeOffset.Parse("2026-10-22T00:00:00Z"),DateTimeOffset.Parse("2026-10-23T00:00:00Z"));
    [Fact]
    public async Task Reintento_ReutilizaInstanciaYNoEmiteDesafios()
    {
        var clock=new Reloj(); var repo=new Repo(); var transport=new Transporte(
            new ResultadoEntregaCorreo(EstadoEntregaCorreo.ResultadoIncierto,MotivoEntregaCorreo.Timeout,intentoHttpIniciado:true),
            new ResultadoEntregaCorreo(EstadoEntregaCorreo.AceptadaPorProveedor,MotivoEntregaCorreo.Ninguno,"ok",intentoHttpIniciado:true));
        var mensaje=Mensaje();
        var run=Procesador(repo,transport,clock).EjecutarAsync(new(clock.GetUtcNow(),clock.GetTimestamp(),mensaje),default);
        await clock.EsperaReintento.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Avanzar(TimeSpan.FromSeconds(1));
        Assert.Equal(FinTrabajoCorreo.Aceptado,await run);
        Assert.Equal(2,transport.Mensajes.Count);
        Assert.All(transport.Mensajes,m=>Assert.Same(mensaje,m));
        Assert.Equal(0,repo.Emisiones);
        Assert.Equal(2,repo.Consultas);
    }
    [Fact]
    public async Task ReemplazadoAntesDeReintento_NoVuelveALlamarProveedor()
    {
        var clock=new Reloj(); var repo=new Repo(); var transport=new Transporte(
            new ResultadoEntregaCorreo(EstadoEntregaCorreo.ResultadoIncierto,MotivoEntregaCorreo.Timeout,intentoHttpIniciado:true));
        var run=Procesador(repo,transport,clock).EjecutarAsync(new(clock.GetUtcNow(),clock.GetTimestamp(),Mensaje()),default);
        await clock.EsperaReintento.Task.WaitAsync(TimeSpan.FromSeconds(5));
        repo.Vigente=false; clock.Avanzar(TimeSpan.FromSeconds(1));
        Assert.Equal(FinTrabajoCorreo.Incierto,await run);
        Assert.Single(transport.Mensajes);
        Assert.Equal(0,repo.Emisiones);
    }
    [Fact]
    public async Task VencidoEnCola_NoConsultaSqlNiProveedor()
    {
        var clock=new Reloj();var repo=new Repo();var transport=new Transporte();
        var trabajo=new TrabajoVerificacionCorreo(clock.GetUtcNow(),clock.GetTimestamp(),Mensaje());
        clock.Avanzar(TimeSpan.FromSeconds(120));
        Assert.Equal(FinTrabajoCorreo.VencidoEnCola,await Procesador(repo,transport,clock).EjecutarAsync(trabajo,default));
        Assert.Equal(0,repo.Consultas);Assert.Empty(transport.Mensajes);
    }
    [Fact]
    public async Task ReenvioNoElegible_NoLlamaAlProveedor()
    {
        var clock=new Reloj();var repo=new Repo();var transport=new Transporte();
        Assert.Equal(FinTrabajoCorreo.NoElegible,await Procesador(repo,transport,clock)
            .EjecutarAsync(new(clock.GetUtcNow(),clock.GetTimestamp(),emailReenvio:"a@example.invalid"),default));
        Assert.Equal(1,repo.Emisiones); Assert.Empty(transport.Mensajes);
    }
    [Fact]
    public async Task Cancelacion_NoIniciaTransporte()
    {
        var clock=new Reloj();var repo=new Repo();var transport=new Transporte();
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Equal(FinTrabajoCorreo.Cancelado,await Procesador(repo,transport,clock)
            .EjecutarAsync(new(clock.GetUtcNow(),clock.GetTimestamp(),Mensaje()),cancel.Token));
        Assert.Empty(transport.Mensajes);
    }
    [Fact]
    public async Task DosFallosTemporales_NoGeneranTercerIntento()
    {
        var clock=new Reloj();var repo=new Repo();
        var transporte=new Transporte(
            new ResultadoEntregaCorreo(EstadoEntregaCorreo.FalloTemporal,MotivoEntregaCorreo.ErrorProveedor),
            new ResultadoEntregaCorreo(EstadoEntregaCorreo.FalloTemporal,MotivoEntregaCorreo.ErrorProveedor));
        var run=Procesador(repo,transporte,clock).EjecutarAsync(new(clock.GetUtcNow(),clock.GetTimestamp(),Mensaje()),default);
        await clock.EsperaReintento.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Avanzar(TimeSpan.FromSeconds(1));
        Assert.Equal(FinTrabajoCorreo.FalloFinal,await run);
        Assert.Equal(2,transporte.Mensajes.Count);Assert.Equal(0,repo.Emisiones);
    }

    [Theory]
    [InlineData(MotivoEntregaCorreo.Autorizacion,false)]
    [InlineData(MotivoEntregaCorreo.Autorizacion,true)]
    [InlineData(MotivoEntregaCorreo.CuotaDiaria,false)]
    [InlineData(MotivoEntregaCorreo.CuotaDiaria,true)]
    [InlineData(MotivoEntregaCorreo.CuotaMensual,false)]
    [InlineData(MotivoEntregaCorreo.CuotaMensual,true)]
    public async Task ProveedorNoDisponible_NoSeConfundeConRechazoNiPierdeIncertidumbre(MotivoEntregaCorreo motivo,bool incierto)
    {
        var clock=new Reloj();var repo=new Repo();var mensaje=Mensaje();
        var noDisponible=new ResultadoEntregaCorreo(EstadoEntregaCorreo.ProveedorNoDisponible,motivo,intentoHttpIniciado:true);
        var transporte=incierto
            ? new Transporte(new ResultadoEntregaCorreo(EstadoEntregaCorreo.ResultadoIncierto,MotivoEntregaCorreo.Timeout,intentoHttpIniciado:true),noDisponible)
            : new Transporte(noDisponible);
        var run=Procesador(repo,transporte,clock).EjecutarAsync(new(clock.GetUtcNow(),clock.GetTimestamp(),mensaje),default);
        if(incierto)
        {
            await clock.EsperaReintento.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Avanzar(TimeSpan.FromSeconds(1));
        }
        var fin=await run;
        Assert.Equal(incierto?FinTrabajoCorreo.ProveedorNoDisponibleIncierto:FinTrabajoCorreo.ProveedorNoDisponible,fin);
        Assert.Equal(incierto?2:1,transporte.Mensajes.Count);
        Assert.All(transporte.Mensajes,m=>Assert.Same(mensaje,m));
        Assert.Equal(0,repo.Emisiones);
    }

    private static ProcesarCorreoVerificacion Procesador(Repo repo,Transporte transporte,Reloj clock) =>
        new(repo,new Tokens(),new Preparador(),transporte,new(),new(),clock);
    private sealed class Repo : IVerificacionCorreoRepository
    {
        public bool Vigente=true; public int Emisiones,Consultas;
        public Task<bool> VigenteParaEnvioAsync(Guid id,string destinatario,CancellationToken ct) { Consultas++;return Task.FromResult(Vigente); }
        public Task<bool> EsquemaDisponibleAsync(CancellationToken ct)=>Task.FromResult(true);
        public Task<EmisionCorreo?> ReenviarAsync(string e,byte[] h,PoliticaVerificacionCorreo p,CancellationToken ct)
            { Emisiones++;return Task.FromResult<EmisionCorreo?>(null); }
        public Task<ReservaRegistroCorreo> RegistrarAsync(Usuario u,byte[] h,PoliticaVerificacionCorreo p,CancellationToken ct)=>throw new NotSupportedException();
        public Task<ResultadoConfirmacionCorreo> ConfirmarAsync(Guid id,byte[] h,string pass,PoliticaVerificacionCorreo p,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Tokens : IGeneradorTokenVerificacion
    { public TokenVerificacion Generar()=>new(new string('A',43),new byte[32]); }
    private sealed class Preparador : IPreparadorMensajeVerificacion
    { public MensajeVerificacionCorreo Preparar(Guid id,string e,string n,string t,DateTimeOffset a,DateTimeOffset b)=>throw new NotSupportedException(); }
    private sealed class Transporte(params ResultadoEntregaCorreo[] resultados) : ITransporteCorreoVerificacion
    {
        public List<MensajeVerificacionCorreo> Mensajes=[];
        public Task<ResultadoEntregaCorreo> EnviarAsync(MensajeVerificacionCorreo m,CancellationToken ct)
        { Mensajes.Add(m);return Task.FromResult(resultados[Mensajes.Count-1]); }
    }
    private sealed class Reloj : TimeProvider
    {
        private long _ticks;
        private readonly List<Temporizador> _timers=[];
        public TaskCompletionSource EsperaReintento=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow()=>DateTimeOffset.Parse("2026-10-22T00:00:00Z").AddTicks(GetTimestamp());
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)
        {
            var timer=new Temporizador(this,callback,state,dueTime);
            lock(_timers) _timers.Add(timer);
            if(dueTime==TimeSpan.FromSeconds(1)) EsperaReintento.TrySetResult();
            return timer;
        }
        public void Avanzar(TimeSpan tiempo)
        {
            Interlocked.Add(ref _ticks,tiempo.Ticks);
            Temporizador[] timers;lock(_timers) timers=_timers.ToArray();
            foreach(var t in timers) t.Disparar();
        }
        private sealed class Temporizador(Reloj clock,TimerCallback callback,object? state,TimeSpan due) : ITimer
        {
            private long _vence=clock.GetTimestamp()+due.Ticks;
            private int _disposed;
            public void Disparar() { if(clock.GetTimestamp()>=_vence && Interlocked.Exchange(ref _disposed,1)==0) callback(state); }
            public bool Change(TimeSpan d,TimeSpan p) { _vence=clock.GetTimestamp()+d.Ticks;return true; }
            public void Dispose()=>Interlocked.Exchange(ref _disposed,1);
            public ValueTask DisposeAsync(){Dispose();return ValueTask.CompletedTask;}
        }
    }
}
