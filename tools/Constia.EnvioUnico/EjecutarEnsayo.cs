using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Constia.Application.Correo;
using Constia.Infrastructure.Correo;

namespace Constia.EnvioUnico;

public sealed record EnsayoPreparado(Guid EnsayoId, Guid OperacionId, string Destinatario,
    string Tipo, byte[] Identidad, SolicitudCorreoPreparada Solicitud)
{
    public const string TipoPermitido = "ensayo-tecnico";
    public string? Remitente { get; init; }
    public static EnsayoPreparado CrearReal(Guid ensayo, Guid operacion, string destinatario, ReadOnlySpan<byte> clave, string remitente)
    {
        if (!DatosVerificacionCorreo.DireccionSimple(remitente)) throw new ArgumentException("Remitente inválido.");
        return Crear(ensayo, operacion, destinatario, clave) with
        { Remitente = remitente, Solicitud = new(operacion.ToString("N"), Payload(destinatario, operacion, remitente)) };
    }
    public static EnsayoPreparado Crear(Guid ensayo, Guid operacion, string destinatario, ReadOnlySpan<byte> clave) =>
        new(ensayo, operacion, destinatario, TipoPermitido, IdentidadDestinatarioEnsayo.Calcular(destinatario, clave),
            new(operacion.ToString("N"), Payload(destinatario)));

    internal static byte[] Payload(string destinatario, Guid operacion = default, string? remitente = null) => remitente is null ? JsonSerializer.SerializeToUtf8Bytes(new
    {
        to = new[] { destinatario }, subject = "CONSTIA — ensayo técnico sin envío",
        text = "Simulación local. No se realizará ninguna solicitud HTTP."
    }) : JsonSerializer.SerializeToUtf8Bytes(new
    {
        from = $"CONSTIA <{remitente}>", to = new[] { destinatario },
        subject = "CONSTIA — ensayo de envío único",
        text = $"Correo técnico de ensayo único de CONSTIA. Operación: {operacion:N}."
    });
    public override string ToString() => "[ensayo redactado]";
}

public interface IAutorizacionEnsayo
{
    Task<bool> ConsumirAsync(Guid ensayo, byte[] identidad, Guid operacion, CancellationToken ct);
    Task<bool> RegistrarAsync(Guid ensayo, Guid operacion, EstadoAutorizacionEnvio resultado, CancellationToken ct);
}

// Adapter only: C127's entry point never constructs a DbContext or this adapter.
public sealed class AutorizacionC109(AutorizacionEnvioUnicoRepository repository) : IAutorizacionEnsayo
{
    public Task<bool> ConsumirAsync(Guid ensayo, byte[] identidad, Guid operacion, CancellationToken ct) =>
        repository.ConsumirAsync(ensayo, identidad, operacion, ct);
    public Task<bool> RegistrarAsync(Guid ensayo, Guid operacion, EstadoAutorizacionEnvio resultado, CancellationToken ct) =>
        repository.RegistrarResultadoAsync(ensayo, operacion, resultado, ct);
}

public sealed record InstalacionEnsayo(Guid InstalacionId, string Raiz)
{
    public const string Archivo = "constia-envio-unico.instalacion.json";
    public static InstalacionEnsayo Cargar()
    {
        var path = Path.Combine(AppContext.BaseDirectory, Archivo);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Configuracion de instalacion no verificable.");
        var config = JsonSerializer.Deserialize<InstalacionEnsayo>(File.ReadAllText(path));
        if (config is null || config.InstalacionId == Guid.Empty || string.IsNullOrWhiteSpace(config.Raiz))
            throw new ArgumentException("Instalacion invalida.");
        return config;
    }
}

public sealed class RegistroInicioLocal
{
    private readonly string carpeta;
    public RegistroInicioLocal(string carpeta)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(carpeta)
            || !Path.IsPathFullyQualified(carpeta) || carpeta.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Se requiere una ruta absoluta Windows local verificable.");
        this.carpeta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(carpeta));
        if (!string.Equals(this.carpeta, Path.TrimEndingDirectorySeparator(carpeta), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(this.carpeta).StartsWith("Constia-C127-", StringComparison.Ordinal))
            throw new ArgumentException("Ruta no canonica o ajena al entorno de ensayo.");
        var drive = new DriveInfo(Path.GetPathRoot(this.carpeta)!);
        if (!drive.IsReady || drive.DriveType != DriveType.Fixed || drive.DriveFormat != "NTFS")
            throw new ArgumentException("Solo almacenamiento local fijo NTFS verificable.");
        for (var directory = new DirectoryInfo(this.carpeta); directory is not null; directory = directory.Parent)
        {
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Directorio inexistente o cadena con enlaces.");
        }
    }

    public bool IntentarIniciar(Guid ensayo)
    {
        if (ensayo == Guid.Empty) return false;
        try
        {
            using var file = new FileStream(Path.Combine(carpeta, ensayo.ToString("N") + ".inicio"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            file.Write(Encoding.ASCII.GetBytes("C127 INICIADO " + ensayo.ToString("N")));
            file.Flush(flushToDisk: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

public enum ModoTransporteFalso { Correcto, Timeout, Error }

public sealed class TransporteFalso(ModoTransporteFalso modo = ModoTransporteFalso.Correcto) : ITransporteCorreoPreparado
{
    private int invocaciones;
    public int Invocaciones => Volatile.Read(ref invocaciones);
    public Task<ResultadoEntregaCorreo> EnviarAsync(SolicitudCorreoPreparada solicitud, CancellationToken ct)
    {
        Interlocked.Increment(ref invocaciones);
        ct.ThrowIfCancellationRequested();
        return modo switch
        {
            ModoTransporteFalso.Timeout => Task.FromException<ResultadoEntregaCorreo>(new TimeoutException("Timeout simulado")),
            ModoTransporteFalso.Error => Task.FromException<ResultadoEntregaCorreo>(new IOException("Error simulado")),
            _ => Task.FromResult(new ResultadoEntregaCorreo(EstadoEntregaCorreo.ResultadoIncierto, MotivoEntregaCorreo.Deshabilitado))
        };
    }
}

public enum FinEnsayo { Invalido, InicioRechazado, NoAutorizado, Cancelado, Simulado, Incierto, RegistroFallido, Aceptado, Rechazado }
public sealed record ResultadoEnsayo(FinEnsayo Fin, ResultadoEntregaCorreo? Entrega = null, bool ResultadoRegistrado = false);

public sealed class EjecutarEnsayo
{
    private readonly RegistroInicioLocal registro;
    private readonly IAutorizacionEnsayo autorizacion;
    private readonly ITransporteCorreoPreparado transporte;
    private readonly string? remitenteReal;
    public EjecutarEnsayo(RegistroInicioLocal registro, IAutorizacionEnsayo autorizacion,
        ITransporteCorreoPreparado transporte, string? remitenteReal = null)
    {
        if (remitenteReal is not null && (autorizacion is not AutorizacionC109 || transporte is not ResendHttpTransport)
            || remitenteReal is null && transporte is ResendHttpTransport)
            throw new ArgumentException("Composición real incompatible.");
        this.registro = registro; this.autorizacion = autorizacion; this.transporte = transporte; this.remitenteReal = remitenteReal;
    }
    public async Task<FinEnsayo> EjecutarAsync(EnsayoPreparado entrada, ReadOnlyMemory<byte> clave, CancellationToken ct) =>
        (await EjecutarConResultadoAsync(entrada, clave, ct)).Fin;

    public async Task<ResultadoEnsayo> EjecutarConResultadoAsync(EnsayoPreparado entrada, ReadOnlyMemory<byte> clave, CancellationToken ct)
    {
        byte[] identidad;
        try
        {
            if (ct.IsCancellationRequested) return new(FinEnsayo.Cancelado);
            if (entrada.EnsayoId == Guid.Empty || entrada.OperacionId == Guid.Empty || entrada.Tipo != EnsayoPreparado.TipoPermitido)
                return new(FinEnsayo.Invalido);
            identidad = IdentidadDestinatarioEnsayo.Calcular(entrada.Destinatario, clave.Span);
            if (!CryptographicOperations.FixedTimeEquals(identidad, entrada.Identidad)
                || entrada.Solicitud.ClaveIdempotencia != entrada.OperacionId.ToString("N")
                || entrada.Remitente != remitenteReal
                || !entrada.Solicitud.CopiarPayload().AsSpan().SequenceEqual(EnsayoPreparado.Payload(entrada.Destinatario, entrada.OperacionId, remitenteReal)))
                return new(FinEnsayo.Invalido);
        }
        catch (ArgumentException) { return new(FinEnsayo.Invalido); }

        if (!registro.IntentarIniciar(entrada.EnsayoId)) return new(FinEnsayo.InicioRechazado);
        try
        {
            if (!await autorizacion.ConsumirAsync(entrada.EnsayoId, identidad, entrada.OperacionId, ct))
                return new(FinEnsayo.NoAutorizado);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { return new(FinEnsayo.NoAutorizado); }

        var fin = FinEnsayo.Simulado;
        ResultadoEntregaCorreo? entrega = null;
        var invocado = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            invocado = true;
            entrega = await transporte.EnviarAsync(entrada.Solicitud, ct);
            fin = transporte is TransporteFalso ? FinEnsayo.Simulado : entrega.Estado switch
            {
                EstadoEntregaCorreo.AceptadaPorProveedor => FinEnsayo.Aceptado,
                EstadoEntregaCorreo.ResultadoIncierto => FinEnsayo.Incierto,
                EstadoEntregaCorreo.CanceladaAntesDelEnvio => FinEnsayo.Cancelado,
                _ => FinEnsayo.Rechazado
            };
        }
        catch (OperationCanceledException)
        {
            fin = invocado ? FinEnsayo.Incierto : FinEnsayo.Cancelado;
            entrega = new(invocado ? EstadoEntregaCorreo.ResultadoIncierto : EstadoEntregaCorreo.CanceladaAntesDelEnvio,
                MotivoEntregaCorreo.Cancelacion, intentoHttpIniciado: invocado);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            fin = FinEnsayo.Incierto;
            entrega = new(EstadoEntregaCorreo.ResultadoIncierto,
                ex is TimeoutException ? MotivoEntregaCorreo.Timeout : MotivoEntregaCorreo.Red, intentoHttpIniciado: invocado);
        }

        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var terminal = entrega?.Estado == EstadoEntregaCorreo.AceptadaPorProveedor
                ? EstadoAutorizacionEnvio.Aceptada : EstadoAutorizacionEnvio.Incierta;
            if (!await autorizacion.RegistrarAsync(entrada.EnsayoId, entrada.OperacionId, terminal, limite.Token))
                return new(FinEnsayo.RegistroFallido, entrega);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { return new(FinEnsayo.RegistroFallido, entrega); }
        return new(fin, entrega, true);
    }
}

public sealed class AutorizacionSimulada(EnsayoPreparado autorizada) : IAutorizacionEnsayo
{
    private int consumida;
    private readonly byte[] identidad = (byte[])autorizada.Identidad.Clone();
    public Task<bool> ConsumirAsync(Guid ensayo, byte[] identidad, Guid operacion, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ensayo == autorizada.EnsayoId && operacion == autorizada.OperacionId
            && CryptographicOperations.FixedTimeEquals(identidad, this.identidad)
            && Interlocked.CompareExchange(ref consumida, 1, 0) == 0);
    }
    public Task<bool> RegistrarAsync(Guid ensayo, Guid operacion, EstadoAutorizacionEnvio resultado, CancellationToken ct) =>
        Task.FromResult(ensayo == autorizada.EnsayoId && operacion == autorizada.OperacionId && Volatile.Read(ref consumida) == 1);
}
