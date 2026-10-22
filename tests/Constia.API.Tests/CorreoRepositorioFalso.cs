using Constia.Application.Correo;
using Constia.Application.Usuarios;
using Constia.Domain;

namespace Constia.API.Tests;

// Persistencia exclusivamente en memoria. Estas pruebas NO acreditan atomicidad SQL.
internal sealed class CorreoRepositorioFalso(JwtApiFactory factory, IUsuarioPasswordHasher hasher, TimeProvider clock)
    : IVerificacionCorreoRepository
{
    public Task<bool> VigenteParaEnvioAsync(Guid id, string destinatario, CancellationToken ct)
    {
        lock(factory.CorreoLock)
        {
            return Task.FromResult(factory.DesafiosCorreo.TryGetValue(id,out var d) &&
                d.EstaVigente(clock.GetUtcNow()) && factory.UsuariosPersistidos.TryGetValue(d.UsuarioId,out var u) &&
                u.EstadoVerificacionCorreo == EstadoVerificacionCorreo.Pendiente &&
                string.Equals(d.EmailDestino,u.Email,StringComparison.Ordinal) &&
                string.Equals(d.EmailDestino,destinatario,StringComparison.Ordinal));
        }
    }
    public Task<bool> EsquemaDisponibleAsync(CancellationToken ct) => Task.FromResult(factory.EsquemaCorreoDisponible);

    public Task<ReservaRegistroCorreo> RegistrarAsync(Usuario u, byte[] hash, PoliticaVerificacionCorreo p, CancellationToken ct)
    {
        lock (factory.CorreoLock)
        {
            if (factory.UsuariosPersistidos.Values.Any(x => x.Email == u.Email))
                return Task.FromResult(new ReservaRegistroCorreo(EstadoReservaCorreo.Duplicado));
            var now = clock.GetUtcNow();
            if (factory.DesafiosCorreo.Values.Count(x => x.EmitidoEnUtc > now.AddHours(-24)) >= p.EmisionesGlobales)
                return Task.FromResult(new ReservaRegistroCorreo(EstadoReservaCorreo.PresupuestoAgotado));
            var d = new DesafioVerificacionCorreo(u.Id,u.Email,hash,now);
            factory.UsuariosPersistidos[u.Id] = u;
            factory.DesafiosCorreo[d.Id] = d;
            return Task.FromResult(new ReservaRegistroCorreo(EstadoReservaCorreo.Creado,new(d.Id,u.Email,u.Nombre,d.EmitidoEnUtc,d.ExpiraEnUtc)));
        }
    }

    public Task<EmisionCorreo?> ReenviarAsync(string email, byte[] hash, PoliticaVerificacionCorreo p, CancellationToken ct)
    {
        lock(factory.CorreoLock)
        {
            var u = factory.UsuariosPersistidos.Values.SingleOrDefault(x => x.Email == email);
            var now = clock.GetUtcNow();
            if(u is null || u.EstadoVerificacionCorreo != EstadoVerificacionCorreo.Pendiente || !DatosVerificacionCorreo.DireccionSimple(u.Email)) return Task.FromResult<EmisionCorreo?>(null);
            var ds = factory.DesafiosCorreo.Values.Where(x => x.UsuarioId == u.Id).ToArray();
            if(!p.PermiteEmision(ds.Select(x => x.EmitidoEnUtc).ToArray(),now) ||
                factory.DesafiosCorreo.Values.Count(x => x.EmitidoEnUtc > now.AddHours(-24)) >= p.EmisionesGlobales)
                return Task.FromResult<EmisionCorreo?>(null);
            var actual = ds.SingleOrDefault(x => x.ConsumidoEnUtc is null && x.InvalidadoEnUtc is null);
            actual?.Invalidar(now);
            var nuevo = new DesafioVerificacionCorreo(u.Id,u.Email,hash,now);
            factory.DesafiosCorreo[nuevo.Id] = nuevo;
            return Task.FromResult<EmisionCorreo?>(new(nuevo.Id,u.Email,u.Nombre,nuevo.EmitidoEnUtc,nuevo.ExpiraEnUtc));
        }
    }

    public Task<ResultadoConfirmacionCorreo> ConfirmarAsync(Guid id, byte[] hash, string password, PoliticaVerificacionCorreo p, CancellationToken ct)
    {
        lock(factory.CorreoLock)
        {
            if(!factory.DesafiosCorreo.TryGetValue(id,out var d)) return Task.FromResult(ResultadoConfirmacionCorreo.Invalido);
            var now = clock.GetUtcNow();
            if(d.ConsumidoEnUtc is not null) return Task.FromResult(ResultadoConfirmacionCorreo.Usado);
            if(d.InvalidadoEnUtc is not null) return Task.FromResult(ResultadoConfirmacionCorreo.Reemplazado);
            if(!d.EstaVigente(now)) return Task.FromResult(ResultadoConfirmacionCorreo.Vencido);
            if(!d.RegistrarIntento(now,p.IntentosPorDesafio,p.VentanaIntentos)) return Task.FromResult(ResultadoConfirmacionCorreo.LimiteIntentos);
            var u = factory.UsuariosPersistidos[d.UsuarioId];
            if(u.EstadoVerificacionCorreo != EstadoVerificacionCorreo.Pendiente || u.Email != d.EmailDestino ||
                !HashTokenVerificacion.Coincide(d.TokenHash,hash) || !hasher.VerifyPassword(u.PasswordHash,password))
                return Task.FromResult(ResultadoConfirmacionCorreo.Invalido);
            u.ConfirmarCorreo(now); d.Consumir(now);
            return Task.FromResult(ResultadoConfirmacionCorreo.Confirmado);
        }
    }
}

internal sealed class EntregaCorreoFalsa(JwtApiFactory factory) : ITransporteCorreoVerificacion
{
    public Task<ResultadoEntregaCorreo> EnviarAsync(MensajeVerificacionCorreo mensaje, CancellationToken ct)
    {
        var token = System.Text.RegularExpressions.Regex.Match(mensaje.TextoPlano,"token=([A-Za-z0-9_-]+)").Groups[1].Value;
        factory.CorreosDePrueba.Enqueue((mensaje.DesafioId,mensaje.Destinatario,token));
        return Task.FromResult(factory.FallarEntregaCorreo
            ? new ResultadoEntregaCorreo(EstadoEntregaCorreo.RechazadaDefinitivamente,MotivoEntregaCorreo.SolicitudRechazada)
            : new ResultadoEntregaCorreo(EstadoEntregaCorreo.AceptadaPorProveedor,MotivoEntregaCorreo.Ninguno,"synthetic",intentoHttpIniciado:true));
    }
}
internal sealed class CanalCorreoFalso : ICanalCorreoDePrueba
{
    private readonly Constia.Infrastructure.Correo.ColaVerificacionCorreoMemoria _cola = new(new());
    public bool Disponible => _cola.Disponible;
    public IReservaTrabajoCorreo? Reservar() => _cola.Reservar();
    public void Iniciar() => _cola.Iniciar();
    public void CerrarAdmision() => _cola.CerrarAdmision();
    public void Fallar() => _cola.Fallar();
    public Task EsperarVacioAsync(CancellationToken ct) => _cola.EsperarVacioAsync(ct);
    public ValueTask<TrabajoVerificacionCorreo?> LeerAsync(CancellationToken ct) => _cola.LeerAsync(ct);
}