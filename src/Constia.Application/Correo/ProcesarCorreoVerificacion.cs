using Constia.Application.Usuarios;
namespace Constia.Application.Correo;

public sealed class ProcesarCorreoVerificacion(IVerificacionCorreoRepository repository,
    IGeneradorTokenVerificacion tokens, IPreparadorMensajeVerificacion preparador,
    ITransporteCorreoVerificacion transporte, PoliticaVerificacionCorreo emision,
    PoliticaProcesamientoCorreo politica, TimeProvider clock)
{
    public async Task<FinTrabajoCorreo> EjecutarAsync(TrabajoVerificacionCorreo trabajo, CancellationToken ct)
    {
        if (clock.GetElapsedTime(trabajo.MarcaAdmision) >= politica.Espera) return FinTrabajoCorreo.VencidoEnCola;
        using var limite = new CancellationTokenSource(politica.Procesamiento, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, limite.Token);
        var estado = new EstadoProcesamientoCorreo();
        var inicio = clock.GetTimestamp();
        try
        {
            var mensaje = trabajo.Mensaje;
            if (mensaje is null)
            {
                var token = tokens.Generar();
                var datos = await repository.ReenviarAsync(trabajo.EmailReenvio!, token.Hash, emision, linked.Token);
                if (datos is null) return FinTrabajoCorreo.NoElegible;
                try { mensaje = preparador.Preparar(datos.DesafioId, datos.Email, datos.Nombre, token.Valor,
                    datos.EmitidoEnUtc, datos.ExpiraEnUtc); }
                catch (ArgumentException) { return FinTrabajoCorreo.PreparacionFallida; }
                catch (InvalidOperationException) { return FinTrabajoCorreo.PreparacionFallida; }
            }
            for (var intento = 0; intento < politica.Intentos; intento++)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (!await repository.VigenteParaEnvioAsync(mensaje.DesafioId, mensaje.Destinatario, linked.Token))
                    return estado.HuboIncertidumbre ? FinTrabajoCorreo.Incierto : FinTrabajoCorreo.Obsoleto;
                var restante = politica.Procesamiento - clock.GetElapsedTime(inicio);
                // C1 usa 10s como máximo cuando C2 está habilitada.
                if (restante < TimeSpan.FromSeconds(10)) break;
                var resultado = await transporte.EnviarAsync(mensaje, linked.Token);
                estado.Registrar(resultado);
                // No reintentar ni emitir nuevos desafíos ante una indisponibilidad conocida.
                if (resultado.Estado is EstadoEntregaCorreo.AceptadaPorProveedor or EstadoEntregaCorreo.ProveedorNoDisponible) break;
                if (intento + 1 >= politica.Intentos) break;
                var espera = politica.EsperaReintento(resultado,
                    politica.Procesamiento - clock.GetElapsedTime(inicio), TimeSpan.FromSeconds(10));
                if (espera is null) break;
                await Task.Delay(espera.Value, clock, linked.Token);
            }
            return estado.Final;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return estado.HuboIncertidumbre ? FinTrabajoCorreo.Incierto : FinTrabajoCorreo.Cancelado;
        }
        catch (CorreoNoDisponibleException)
        {
            return estado.HuboIncertidumbre ? FinTrabajoCorreo.Incierto : FinTrabajoCorreo.FalloInfraestructura;
        }
    }
}
