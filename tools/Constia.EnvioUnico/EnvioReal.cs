using System.Security.Cryptography;
using Constia.Infrastructure;
using Constia.Infrastructure.Correo;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace Constia.EnvioUnico;

public sealed class ConfiguracionEnvioReal
{
    public OpcionesResend Resend { get; }
    public string ConexionSql { get; }
    public byte[] ClaveHmac { get; }
    private ConfiguracionEnvioReal(OpcionesResend resend, string sql, byte[] clave)
    { Resend = resend; ConexionSql = sql; ClaveHmac = clave; }
    public override string ToString() => "[configuración de envío redactada]";
    public static ConfiguracionEnvioReal Cargar(InstalacionEnsayo instalacion, Guid ensayo, Guid operacion,
        string destinatario, Func<string, string?> leer)
    {
        if (ensayo == Guid.Empty || operacion == Guid.Empty || instalacion.InstalacionId == Guid.Empty
            || !Guid.TryParse(leer("CONSTIA_ENVIO_INSTALACION_ID"), out var id) || id != instalacion.InstalacionId
            || !Constia.Application.Correo.DatosVerificacionCorreo.DireccionSimple(destinatario)
            || leer("CONSTIA_ENVIO_DESTINATARIO") != destinatario)
            throw new ArgumentException("Identidad de ensayo o instalación inválida.");
        var opciones = new OpcionesResend
        {
            Enabled = bool.TryParse(leer("Resend__Enabled"), out var enabled) && enabled,
            ApiKey = leer("Resend__ApiKey"), FromAddress = leer("Resend__FromAddress"),
            FromName = leer("Resend__FromName") ?? "CONSTIA",
            RequestTimeoutSeconds = int.TryParse(leer("Resend__RequestTimeoutSeconds") ?? "10", out var timeout) ? timeout : 0
        };
        if (!opciones.Enabled || ValidadorOpcionesResend.ValidarTransporte(opciones).Count != 0)
            throw new ArgumentException("Configuración de transporte inválida.");
        var sql = new SqlConnectionStringBuilder(leer("CONSTIA_ENVIO_SQL") ?? "");
        if (string.IsNullOrWhiteSpace(sql.DataSource) || string.IsNullOrWhiteSpace(sql.InitialCatalog)
            || new[] { "master", "model", "msdb", "tempdb" }.Contains(sql.InitialCatalog, StringComparer.OrdinalIgnoreCase)
            || sql.ConnectTimeout is < 1 or > 30)
            throw new ArgumentException("Conexión SQL de ensayo inválida.");
        sql.ConnectRetryCount = 0;
        sql.ApplicationName = "CONSTIA.EnvioUnico";
        byte[] clave;
        try { clave = Convert.FromBase64String(leer("CONSTIA_ENVIO_HMAC_BASE64") ?? ""); }
        catch (FormatException) { throw new ArgumentException("Clave HMAC inválida."); }
        if (clave.Length < 32)
        {
            CryptographicOperations.ZeroMemory(clave);
            throw new ArgumentException("Clave HMAC inválida.");
        }
        return new(opciones, sql.ConnectionString, clave);
    }
}

// Composition only: construction never connects, migrates or prepares authorizations.
public sealed class EntornoEnvioReal : IDisposable
{
    private readonly ConstiaDbContext db;
    private readonly HttpClient http;
    public ResendHttpTransport Transporte { get; }
    public AutorizacionC109 Autorizacion { get; }
    public EjecutarEnsayo Coordinador { get; }
    public EntornoEnvioReal(ConfiguracionEnvioReal config, InstalacionEnsayo instalacion, HttpMessageHandler? handler = null)
    {
        var registro = new RegistroInicioLocal(instalacion.Raiz);
        db = new ConstiaDbContext(new DbContextOptionsBuilder<ConstiaDbContext>()
            .UseSqlServer(config.ConexionSql, sql => sql.CommandTimeout(15)).Options);
        http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = Timeout.InfiniteTimeSpan };
        Transporte = new ResendHttpTransport(http, config.Resend, TimeProvider.System);
        Autorizacion = new AutorizacionC109(new AutorizacionEnvioUnicoRepository(db, TimeProvider.System));
        Coordinador = new EjecutarEnsayo(registro, Autorizacion, Transporte, config.Resend.FromAddress!);
    }
    public void Dispose() { http.Dispose(); db.Dispose(); }
}

