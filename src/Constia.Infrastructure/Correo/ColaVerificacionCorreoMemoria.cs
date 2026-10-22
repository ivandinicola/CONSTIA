using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using Constia.Application.Correo;
namespace Constia.Infrastructure.Correo;

/// <summary>Un proceso, un lector. Capacidad incluye productores, cola y ejecución.</summary>
public sealed class ColaVerificacionCorreoMemoria(PoliticaProcesamientoCorreo politica, ILogger<ColaVerificacionCorreoMemoria>? logger = null) : ICanalVerificacionCorreo
{
    private readonly object _gate = new();
    private readonly Channel<TrabajoVerificacionCorreo> _canal = Channel.CreateBounded<TrabajoVerificacionCorreo>(
        new BoundedChannelOptions(politica.Capacidad) { SingleReader = false, FullMode = BoundedChannelFullMode.Wait });
    private bool _activo, _cerrado;
    private int _ocupadas;
    private TaskCompletionSource _vacio = Completado();
    private long _admitidos, _finalizados, _fallidos, _incidentes;
    public long Incidentes => Interlocked.Read(ref _incidentes);
    public long Admitidos => Interlocked.Read(ref _admitidos);
    public long Finalizados => Interlocked.Read(ref _finalizados);
    public long Fallidos => Interlocked.Read(ref _fallidos);
    public bool Disponible { get { lock (_gate) return _activo && !_cerrado; } }
    public void Iniciar() { lock (_gate) { if (!_cerrado) _activo = true; } }
    public IReservaTrabajoCorreo? Reservar()
    {
        lock (_gate)
        {
            if (!_activo || _cerrado || _ocupadas >= politica.Capacidad) return null;
            if (_ocupadas++ == 0) _vacio = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new Reserva(this);
        }
    }
    public void CerrarAdmision()
    {
        lock (_gate) { _cerrado = true; if (_ocupadas == 0) _canal.Writer.TryComplete(); }
    }
    public void Fallar()
    {
        lock (_gate)
        {
            if (!_cerrado)
            {
                Interlocked.Increment(ref _incidentes);
                logger?.LogError("Canal de correo cerrado por incidente de procesamiento.");
            }
            _activo = false; _cerrado = true;
            _canal.Writer.TryComplete();
            while (_canal.Reader.TryRead(out var trabajo)) trabajo.Finalizar(FinTrabajoCorreo.FalloInfraestructura);
        }
    }
    public Task EsperarVacioAsync(CancellationToken ct) { lock (_gate) return _vacio.Task.WaitAsync(ct); }
    public async ValueTask<TrabajoVerificacionCorreo?> LeerAsync(CancellationToken ct)
    {
        while (await _canal.Reader.WaitToReadAsync(ct))
            if (_canal.Reader.TryRead(out var trabajo)) return trabajo;
        return null;
    }
    private void Liberar(bool publicado, FinTrabajoCorreo motivo)
    {
        lock (_gate)
        {
            if (publicado)
            {
                Interlocked.Increment(ref _finalizados);
                if (motivo is not (FinTrabajoCorreo.Aceptado or FinTrabajoCorreo.NoElegible or FinTrabajoCorreo.Obsoleto))
                    Interlocked.Increment(ref _fallidos);
            }
            if (--_ocupadas == 0)
            {
                _vacio.TrySetResult();
                if (_cerrado) _canal.Writer.TryComplete();
            }
        }
    }
    private static TaskCompletionSource Completado()
    {
        var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); t.SetResult(); return t;
    }
    private sealed class Reserva(ColaVerificacionCorreoMemoria cola) : IReservaTrabajoCorreo
    {
        private int _estado;
        public void Publicar(TrabajoVerificacionCorreo trabajo)
        {
            lock (cola._gate)
            {
                if (!cola._activo || _estado != 0)
                    throw new InvalidOperationException("Reserva no disponible.");
                trabajo.VincularFinalizacion(m => cola.Liberar(true, m));
                _estado = 1;
                Interlocked.Increment(ref cola._admitidos);
                if (!cola._canal.Writer.TryWrite(trabajo))
                {
                    cola._activo = false;
                    trabajo.Finalizar(FinTrabajoCorreo.FalloInfraestructura);
                    throw new InvalidOperationException("Publicación no disponible.");
                }

            }
        }
        public void Dispose()
        {
            lock (cola._gate)
            {
                if (_estado == 0) { _estado = 2; cola.Liberar(false, default); }
            }
        }
    }
}
