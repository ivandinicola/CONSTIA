using System.Text.Json;
using Constia.Application.Correo;
namespace Constia.Infrastructure.Correo;

public static class ClasificadorRespuestaResend
{
    public static ResultadoEntregaCorreo Clasificar(int status, ReadOnlyMemory<byte> body, TimeSpan? espera)
    {
        ResultadoEntregaCorreo R(EstadoEntregaCorreo estado, MotivoEntregaCorreo motivo, string? id = null) =>
            new(estado, motivo, id, status, espera, intentoHttpIniciado: true);
        if (status is 401 or 403) return R(EstadoEntregaCorreo.ProveedorNoDisponible, MotivoEntregaCorreo.Autorizacion);
        try
        {
            using var doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return R(EstadoEntregaCorreo.ResultadoIncierto, MotivoEntregaCorreo.RespuestaMalformada);
            string? id = null;
            if (doc.RootElement.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String &&
                ResultadoEntregaCorreo.IdValido(value.GetString())) id = value.GetString();
            if (status == 200)
                return id is not null ? R(EstadoEntregaCorreo.AceptadaPorProveedor, MotivoEntregaCorreo.Ninguno, id) :
                    R(EstadoEntregaCorreo.ResultadoIncierto, MotivoEntregaCorreo.RespuestaMalformada);
            var code = doc.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString() : null;
            return (status, code) switch
            {
                (400, "validation_error" or "invalid_idempotency_key") or
                (422, "validation_error" or "invalid_parameter" or "missing_required_field" or "missing_required_parameter")
                    => R(EstadoEntregaCorreo.RechazadaDefinitivamente, MotivoEntregaCorreo.SolicitudRechazada),
                (409, "invalid_idempotent_request") => R(EstadoEntregaCorreo.RechazadaDefinitivamente, MotivoEntregaCorreo.IdempotenciaIncompatible),
                (409, "concurrent_idempotent_requests") => R(EstadoEntregaCorreo.FalloTemporal, MotivoEntregaCorreo.IdempotenciaEnCurso),
                (429, "rate_limit_exceeded") => R(EstadoEntregaCorreo.FalloTemporal, MotivoEntregaCorreo.LimiteVelocidad),
                (429, "daily_quota_exceeded") => R(EstadoEntregaCorreo.ProveedorNoDisponible, MotivoEntregaCorreo.CuotaDiaria),
                (429, "monthly_quota_exceeded") => R(EstadoEntregaCorreo.ProveedorNoDisponible, MotivoEntregaCorreo.CuotaMensual),
                (>= 500 and <= 599, "application_error" or "service_unavailable") =>
                    R(EstadoEntregaCorreo.ResultadoIncierto, MotivoEntregaCorreo.ErrorProveedor),
                _ => R(EstadoEntregaCorreo.ResultadoIncierto, MotivoEntregaCorreo.RespuestaDesconocida, id)
            };
        }
        catch (JsonException)
        {
            return R(EstadoEntregaCorreo.ResultadoIncierto, MotivoEntregaCorreo.RespuestaMalformada);
        }
    }
}
