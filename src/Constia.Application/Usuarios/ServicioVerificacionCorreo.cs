using Constia.Domain;
using Constia.Application.Correo;
namespace Constia.Application.Usuarios;

public sealed class ServicioVerificacionCorreo(
    IVerificacionCorreoRepository repository, IGeneradorTokenVerificacion tokens,
    ICanalVerificacionCorreo canal, IPreparadorMensajeVerificacion preparador,
    IActivacionCorreo activacion, PoliticaVerificacionCorreo politica,
    PoliticaProcesamientoCorreo procesamiento, TimeProvider clock) : IRegistroUsuarioCorreo
{
    public Task<bool> DisponibleAsync(CancellationToken ct) => activacion.HabilitadoAsync(ct);
    public async Task<bool> RegistrarAsync(Usuario usuario, CancellationToken ct)
    {
        if (!await DisponibleAsync(ct)) throw new CorreoNoDisponibleException();
        DatosVerificacionCorreo.ValidarAlta(usuario.Nombre, usuario.Email);
        using var plaza = canal.Reservar() ?? throw new CorreoNoDisponibleException();
        using var limite = new CancellationTokenSource(procesamiento.Sql, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, limite.Token);
        var admitido = clock.GetUtcNow();
        var marca = clock.GetTimestamp();
        var token = tokens.Generar();
        // No reintentar esta transacción: su commit puede ser incierto.
        ReservaRegistroCorreo reserva;
        try { reserva = await repository.RegistrarAsync(usuario, token.Hash, politica, linked.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // El timeout interno no prueba rollback: cerrar la operación sin repetir SQL.
            throw new CorreoNoDisponibleException();
        }
        if (reserva.Estado == EstadoReservaCorreo.Duplicado) return false;
        if (reserva.Estado != EstadoReservaCorreo.Creado) throw new CorreoNoDisponibleException();
        // Desde este punto la cuenta existe; no usar RequestAborted ni revertir el presupuesto.
        try
        {
            var datos = reserva.Emision!;
            var mensaje = preparador.Preparar(datos.DesafioId, datos.Email, datos.Nombre, token.Valor,
                datos.EmitidoEnUtc, datos.ExpiraEnUtc);
            if (clock.GetElapsedTime(marca) >= procesamiento.Reserva) throw new InvalidOperationException("Reserva vencida.");
            plaza.Publicar(new(admitido, marca, mensaje));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Incidente de preparación/publicación: cerrar admisión, sin incluir datos sensibles.
            canal.Fallar();
        }
        return true;
    }
    public async Task ReenviarAsync(string email, CancellationToken ct)
    {
        if (!await DisponibleAsync(ct)) throw new CorreoNoDisponibleException();
        ct.ThrowIfCancellationRequested();
        using var plaza = canal.Reservar() ?? throw new CorreoNoDisponibleException();
        plaza.Publicar(new(clock.GetUtcNow(), clock.GetTimestamp(), emailReenvio: email));
    }
    public Task<ResultadoConfirmacionCorreo> ConfirmarAsync(Guid id, string token, string password, CancellationToken ct)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(password) || !politica.EsValida ||
            !HashTokenVerificacion.IntentarCalcular(token, out var hash))
            return Task.FromResult(ResultadoConfirmacionCorreo.Invalido);
        return repository.ConfirmarAsync(id, hash, password, politica, ct);
    }
}
