using Constia.Application.Correo;
using Constia.Infrastructure.Correo;
namespace Constia.API.Tests;
public sealed class ColaVerificacionCorreoTests
{
    private static TrabajoVerificacionCorreo Trabajo() => new(DateTimeOffset.UtcNow,
        TimeProvider.System.GetTimestamp(),emailReenvio:"synthetic@example.invalid");
    [Fact]
    public async Task CuatroPlazas_IncluyenReservasPendientesYEjecucion()
    {
        var cola=new ColaVerificacionCorreoMemoria(new());cola.Iniciar();
        var reservas=Enumerable.Range(0,4).Select(_=>cola.Reservar()!).ToArray();
        Assert.Null(cola.Reservar());
        var trabajo=Trabajo();reservas[0].Publicar(trabajo);reservas[0].Dispose();
        Assert.Null(cola.Reservar());
        Assert.Same(trabajo,await cola.LeerAsync(default));
        Assert.Null(cola.Reservar()); // Leer no libera la ejecución.
        trabajo.Finalizar(FinTrabajoCorreo.Aceptado);
        trabajo.Finalizar(FinTrabajoCorreo.Aceptado); // Cierre idempotente.
        using var nueva=cola.Reservar();Assert.NotNull(nueva);
        foreach(var r in reservas) r.Dispose();
        nueva.Dispose();await cola.EsperarVacioAsync(default);
        Assert.Equal(1,cola.Admitidos);Assert.Equal(1,cola.Finalizados);
    }
    [Fact]
    public async Task Cierre_AdmitePublicacionReservadaYDrenaSinDescartar()
    {
        var cola=new ColaVerificacionCorreoMemoria(new());cola.Iniciar();
        using var reserva=cola.Reservar()!;cola.CerrarAdmision();
        Assert.Null(cola.Reservar());
        var trabajo=Trabajo();reserva.Publicar(trabajo);
        var vacio=cola.EsperarVacioAsync(default);Assert.False(vacio.IsCompleted);
        Assert.Same(trabajo,await cola.LeerAsync(default));
        trabajo.Finalizar(FinTrabajoCorreo.NoElegible);
        await vacio;Assert.Null(await cola.LeerAsync(default));
    }
    [Fact]
    public async Task Fallo_NoLiberaTrabajoQueContinuaActivo()
    {
        var cola=new ColaVerificacionCorreoMemoria(new());cola.Iniciar();
        using var reserva=cola.Reservar()!;var trabajo=Trabajo();reserva.Publicar(trabajo);
        Assert.Same(trabajo,await cola.LeerAsync(default));cola.Fallar();
        Assert.Null(cola.Reservar());var vacio=cola.EsperarVacioAsync(default);
        Assert.False(vacio.IsCompleted);
        trabajo.Finalizar(FinTrabajoCorreo.Incierto);await vacio;
        Assert.Equal(1,cola.Fallidos);
    }
    [Fact]
    public async Task TrabajoYaPublicado_NoFiltraLaSegundaReserva()
    {
        var cola=new ColaVerificacionCorreoMemoria(new());cola.Iniciar();
        using var primera=cola.Reservar()!;var trabajo=Trabajo();primera.Publicar(trabajo);
        using(var segunda=cola.Reservar()!)
            Assert.Throws<InvalidOperationException>(()=>segunda.Publicar(trabajo));
        Assert.Same(trabajo,await cola.LeerAsync(default));
        trabajo.Finalizar(FinTrabajoCorreo.Aceptado);
        await cola.EsperarVacioAsync(default);
        Assert.Equal(1,cola.Admitidos);Assert.Equal(1,cola.Finalizados);
    }
    [Fact]
    public async Task Rafaga32_Rechaza28AntesDeEmitirYDrenaCuatro()
    {
        var cola=new ColaVerificacionCorreoMemoria(new());cola.Iniciar();
        var reservas=new List<IReservaTrabajoCorreo>();var rechazados=0;
        for(var i=0;i<32;i++)
        {
            var reserva=cola.Reservar();
            if(reserva is null) rechazados++;else reservas.Add(reserva);
        }
        Assert.Equal(28,rechazados);
        foreach(var r in reservas){r.Publicar(Trabajo());r.Dispose();}
        cola.CerrarAdmision();
        for(var i=0;i<4;i++)(await cola.LeerAsync(default))!.Finalizar(FinTrabajoCorreo.NoElegible);
        await cola.EsperarVacioAsync(default);
        Assert.Equal(4,cola.Admitidos);Assert.Equal(4,cola.Finalizados);
        Assert.Null(await cola.LeerAsync(default));
    }
    internal sealed class RelojCorreo : TimeProvider
    {
        private long _ticks;
        private readonly List<Temporizador> _timers=[];
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow()=>DateTimeOffset.Parse("2026-10-10T02:30:00Z").AddTicks(GetTimestamp());
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)
        {
            var timer=new Temporizador(this,callback,state,dueTime);
            lock(_timers) _timers.Add(timer);
            return timer;
        }
        public void Avanzar(TimeSpan tiempo)
        {
            Interlocked.Add(ref _ticks,tiempo.Ticks);
            Temporizador[] timers;lock(_timers) timers=_timers.ToArray();
            foreach(var t in timers) t.Disparar();
        }
        private sealed class Temporizador(RelojCorreo clock,TimerCallback callback,object? state,TimeSpan due) : ITimer
        {
            private long _vence=clock.GetTimestamp()+due.Ticks;
            private int _disposed;
            public void Disparar() { if(clock.GetTimestamp()>=_vence && Interlocked.Exchange(ref _disposed,1)==0) callback(state); }
            public bool Change(TimeSpan d,TimeSpan p) { _vence=clock.GetTimestamp()+d.Ticks;return true; }
            public void Dispose()=>Interlocked.Exchange(ref _disposed,1);
            public ValueTask DisposeAsync(){Dispose();return ValueTask.CompletedTask;}
        }
    }}
