using Constia.Domain;
namespace Constia.Application.Usuarios;

public sealed class ServicioVerificacionCorreo(
    IVerificacionCorreoRepository repository,
    IGeneradorTokenVerificacion tokens,
    IEntregaCorreoVerificacion entrega,
    IActivacionCorreo activacion,
    PoliticaVerificacionCorreo politica) : IRegistroUsuarioCorreo
{
    public Task<bool> DisponibleAsync(CancellationToken ct) => activacion.HabilitadoAsync(ct);

    public async Task<bool> RegistrarAsync(Usuario usuario, CancellationToken ct)
    {
        if (!await DisponibleAsync(ct)) throw new CorreoNoDisponibleException();
        var token = tokens.Generar();
        var reserva = await repository.RegistrarAsync(usuario, token.Hash, politica, ct);
        if (reserva.Estado == EstadoReservaCorreo.Duplicado) return false;
        if (reserva.Estado != EstadoReservaCorreo.Creado) throw new CorreoNoDisponibleException();
        // El commit ya finalizó: un fallo de entrega no borra la cuenta ni libera el presupuesto.
        await entrega.IntentarEntregarAsync(reserva.Emision!, token.Valor, ct);
        return true;
    }

    public async Task ReenviarAsync(string email, CancellationToken ct)
    {
        if (!await DisponibleAsync(ct)) throw new CorreoNoDisponibleException();
        var token = tokens.Generar();
        var emision = await repository.ReenviarAsync(email, token.Hash, politica, ct);
        if (emision is not null) await entrega.IntentarEntregarAsync(emision, token.Valor, ct);
    }

    public Task<ResultadoConfirmacionCorreo> ConfirmarAsync(Guid id, string token, string password, CancellationToken ct)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(password) || !politica.EsValida ||
            !HashTokenVerificacion.IntentarCalcular(token, out var hash))
            return Task.FromResult(ResultadoConfirmacionCorreo.Invalido);
        return repository.ConfirmarAsync(id, hash, password, politica, ct);
    }
}
