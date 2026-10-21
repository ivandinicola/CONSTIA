using Constia.Domain;
namespace Constia.Application.Usuarios;

public enum EstadoReservaCorreo { Creado, Duplicado, PresupuestoAgotado }
public sealed record EmisionCorreo(Guid DesafioId, string Email);
public sealed record ReservaRegistroCorreo(EstadoReservaCorreo Estado, EmisionCorreo? Emision = null);
public enum ResultadoConfirmacionCorreo { Confirmado, Invalido, Vencido, Usado, Reemplazado, LimiteIntentos }

public interface IVerificacionCorreoRepository
{
    Task<bool> EsquemaDisponibleAsync(CancellationToken ct);
    Task<ReservaRegistroCorreo> RegistrarAsync(Usuario usuario, byte[] hash, PoliticaVerificacionCorreo politica, CancellationToken ct);
    Task<EmisionCorreo?> ReenviarAsync(string email, byte[] hash, PoliticaVerificacionCorreo politica, CancellationToken ct);
    Task<ResultadoConfirmacionCorreo> ConfirmarAsync(Guid desafioId, byte[] hash, string password, PoliticaVerificacionCorreo politica, CancellationToken ct);
}

public interface IActivacionCorreo
{
    Task<bool> HabilitadoAsync(CancellationToken ct);
}

public interface IEntregaCorreoVerificacion
{
    // La implementación futura devuelve false ante fallo de entrega; nunca registra contenido ni tokens.
    Task<bool> IntentarEntregarAsync(EmisionCorreo emision, string token, CancellationToken ct);
}

// Marcadores explícitos: un capturador de pruebas nunca habilita producción.
public interface IEntregaCorreoDePrueba : IEntregaCorreoVerificacion { }
public interface IEntregaCorreoProduccion : IEntregaCorreoVerificacion
{
    bool Operativo { get; }
}

public interface IRegistroUsuarioCorreo
{
    Task<bool> DisponibleAsync(CancellationToken ct);
    Task<bool> RegistrarAsync(Usuario usuario, CancellationToken ct);
}

public sealed class CorreoNoDisponibleException : Exception
{
    public CorreoNoDisponibleException() : base("La operación de correo no está disponible.") { }
}
