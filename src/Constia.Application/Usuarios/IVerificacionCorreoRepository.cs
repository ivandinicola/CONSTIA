using Constia.Domain;
namespace Constia.Application.Usuarios;

public enum EstadoReservaCorreo { Creado, Duplicado, PresupuestoAgotado }
public sealed record EmisionCorreo(Guid DesafioId, string Email, string Nombre, DateTimeOffset EmitidoEnUtc, DateTimeOffset ExpiraEnUtc)
{
    public override string ToString() => "[emisión de correo redactada]";
}
public sealed record ReservaRegistroCorreo(EstadoReservaCorreo Estado, EmisionCorreo? Emision = null);
public enum ResultadoConfirmacionCorreo { Confirmado, Invalido, Vencido, Usado, Reemplazado, LimiteIntentos }

public interface IVerificacionCorreoRepository
{
    Task<bool> VigenteParaEnvioAsync(Guid desafioId, string destinatario, CancellationToken ct);
    Task<bool> EsquemaDisponibleAsync(CancellationToken ct);
    Task<ReservaRegistroCorreo> RegistrarAsync(Usuario usuario, byte[] hash, PoliticaVerificacionCorreo politica, CancellationToken ct);
    Task<EmisionCorreo?> ReenviarAsync(string email, byte[] hash, PoliticaVerificacionCorreo politica, CancellationToken ct);
    Task<ResultadoConfirmacionCorreo> ConfirmarAsync(Guid desafioId, byte[] hash, string password, PoliticaVerificacionCorreo politica, CancellationToken ct);
}

public interface IActivacionCorreo
{
    Task<bool> HabilitadoAsync(CancellationToken ct);
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
