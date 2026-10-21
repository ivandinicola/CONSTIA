namespace Constia.Application.Correo;

public enum EstadoEntregaCorreo
{
    AceptadaPorProveedor, RechazadaDefinitivamente, FalloTemporal,
    ResultadoIncierto, ProveedorNoDisponible, CanceladaAntesDelEnvio
}

public enum MotivoEntregaCorreo
{
    Ninguno, Deshabilitado, ConfiguracionInvalida, MensajeInvalido,
    SolicitudRechazada, Autorizacion, IdempotenciaIncompatible, IdempotenciaEnCurso,
    LimiteVelocidad, CuotaDiaria, CuotaMensual, ErrorProveedor, Dns, Tls,
    Red, Timeout, Cancelacion, RespuestaMalformada, RespuestaDesconocida, RespuestaDemasiadoGrande
}

public sealed class ResultadoEntregaCorreo
{
    public EstadoEntregaCorreo Estado { get; }
    public MotivoEntregaCorreo Motivo { get; }
    public string? IdProveedor { get; }
    public int? HttpStatus { get; }
    public TimeSpan? EsperaMinima { get; }
    public bool IntentoHttpIniciado { get; }

    public ResultadoEntregaCorreo(EstadoEntregaCorreo estado, MotivoEntregaCorreo motivo,
        string? idProveedor = null, int? httpStatus = null, TimeSpan? esperaMinima = null,
        bool intentoHttpIniciado = false)
    {
        if (!Enum.IsDefined(estado) || !Enum.IsDefined(motivo) ||
            (idProveedor is not null && !IdValido(idProveedor)) ||
            (estado == EstadoEntregaCorreo.AceptadaPorProveedor && (idProveedor is null || !intentoHttpIniciado)) ||
            (estado == EstadoEntregaCorreo.CanceladaAntesDelEnvio && intentoHttpIniciado) ||
            (httpStatus.HasValue && httpStatus.Value is < 100 or > 599) ||
            (esperaMinima.HasValue && esperaMinima.Value < TimeSpan.Zero))
            throw new ArgumentException("Resultado de entrega inválido.");
        Estado = estado; Motivo = motivo; IdProveedor = idProveedor; HttpStatus = httpStatus;
        EsperaMinima = esperaMinima; IntentoHttpIniciado = intentoHttpIniciado;
    }

    // ID opaco: sin exigir UUID. Lista conservadora para metadatos diagnósticos.
    public static bool IdValido(string? id) => id is { Length: > 0 and <= 256 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public override string ToString() => $"Entrega: {Estado}; motivo: {Motivo}; HTTP: {HttpStatus}";
}
