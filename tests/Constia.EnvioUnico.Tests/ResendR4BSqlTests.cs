using System.Net;
using System.Security.Cryptography;
using Constia.Application.Correo;
using Constia.EnvioUnico;
using Constia.Infrastructure;
using Constia.Infrastructure.Correo;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit.Abstractions;

namespace Constia.EnvioUnico.Tests;

public sealed class ResendR4BSqlTests(ITestOutputHelper output)
{
    private const string Migracion = "20261030232834_AddAutorizacionEnvioUnico";
    private const string Destinatario = "resend-r4b@example.invalid";
    private const string Aceptacion = "r4b-accepted-synthetic";

    private sealed class R4BFact : FactAttribute
    {
        public R4BFact()
        {
            if (Environment.GetEnvironmentVariable("CONSTIA_R4B_SQL_TEST") != "1")
                Skip = "Requiere un contenedor temporal R4B verificado.";
        }
    }

    [R4BFact]
    public async Task Consumo_confirmado_antes_de_HTTP_interceptado_y_segundo_intento_bloqueado()
    {
        using var plazo = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = plazo.Token;
        var token = Guid.ParseExact(Environment.GetEnvironmentVariable("CONSTIA_R4B_TOKEN")!, "N").ToString("N");
        var host = "r4b-" + token[..11];
        var puerto = int.Parse(Environment.GetEnvironmentVariable("CONSTIA_R4B_PORT")!);
        Assert.InRange(puerto, 1, 65535);
        var admin = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CONSTIA_R4B_ADMIN"))
        {
            Pooling = false, ConnectRetryCount = 0, Enlist = false
        };
        if (admin.DataSource != "127.0.0.1," + puerto || admin.InitialCatalog != "master"
            || admin.UserID != "sa" || string.IsNullOrWhiteSpace(admin.Password))
            throw new InvalidOperationException("Destino SQL R4B inválido.");

        var baseNombre = "Constia_R4B_" + token;
        var usuario = "R4B_" + token;
        var ensayo = Guid.NewGuid();
        var operacion = Guid.NewGuid();
        var clave = RandomNumberGenerator.GetBytes(32);
        var carpeta = Path.Combine(Path.GetTempPath(), "Constia-C127-ResendR4B-" + token);
        var marcador = Path.Combine(carpeta, ensayo.ToString("N") + ".inicio");
        var raizCreada = false;
        ConfiguracionEnvioReal? config = null;
        try
        {
            await using var conexion = new SqlConnection(admin.ConnectionString);
            for (var intento = 1; ; intento++)
            {
                try
                {
                    await conexion.OpenAsync(ct);
                    output.WriteLine($"Autenticación de readiness: intento {intento}, correcta.");
                    break;
                }
                catch (SqlException e) when (e.Number == 18456 && intento < 8)
                {
                    output.WriteLine($"Autenticación de readiness: intento {intento}, SQL {e.Number}.");
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }
            await using (var comando = new SqlCommand("""
                SELECT CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),
                       CONVERT(int,SERVERPROPERTY('ProductMajorVersion')),
                       CONVERT(nvarchar(128),SERVERPROPERTY('ProductVersion')),
                       (SELECT COUNT(*) FROM sys.databases WHERE database_id > 4);
                """, conexion))
            await using (var lectura = await comando.ExecuteReaderAsync(ct))
            {
                Assert.True(await lectura.ReadAsync(ct));
                Assert.Equal(host, lectura.GetString(0));
                Assert.Equal(16, lectura.GetInt32(1));
                Assert.Equal(0, lectura.GetInt32(3));
                output.WriteLine($"SQL Server {lectura.GetString(2)}; host={host}; bases de usuario iniciales=0.");
            }
            await Ejecutar(conexion, $"CREATE DATABASE [{baseNombre}];", ct);
            var preparador = new SqlConnectionStringBuilder(admin.ConnectionString) { InitialCatalog = baseNombre };
            var identidad = IdentidadDestinatarioEnsayo.Calcular(Destinatario, clave);
            await using (var db = Contexto(preparador.ConnectionString))
            {
                await db.GetService<IMigrator>().MigrateAsync(Migracion, ct);
                var migraciones = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
                Assert.Equal(Migracion, migraciones[^1]);
                Assert.False(await db.Set<AutorizacionEnvioUnico>().AnyAsync(ct));
                db.Add(new AutorizacionEnvioUnico(ensayo, identidad, operacion, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync(ct);
                output.WriteLine($"Base={baseNombre}; migraciones aplicadas={migraciones.Length}; última={Migracion}.");
            }

            var password = "R4B!a9" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await Ejecutar(conexion, $"CREATE LOGIN [{usuario}] WITH PASSWORD='{password}', CHECK_POLICY=OFF, DEFAULT_DATABASE=[{baseNombre}];", ct);
            await using (var db = new SqlConnection(preparador.ConnectionString))
            {
                await db.OpenAsync(ct);
                await Ejecutar(db, $"CREATE USER [{usuario}] FOR LOGIN [{usuario}]; GRANT SELECT ON dbo.AutorizacionEnvioUnico TO [{usuario}]; GRANT UPDATE (Estado,ConsumidaEnUtc,ResultadoEnUtc) ON dbo.AutorizacionEnvioUnico TO [{usuario}];", ct);
            }
            var consumidor = new SqlConnectionStringBuilder(preparador.ConnectionString) { UserID = usuario, Password = password };
            async Task<AutorizacionEnvioUnico> Leer()
            {
                await using var db = Contexto(consumidor.ConnectionString);
                var fila = await db.Set<AutorizacionEnvioUnico>().AsNoTracking().SingleAsync(ct);
                Assert.Equal(ensayo, fila.EnsayoId);
                Assert.Equal(operacion, fila.OperacionId);
                Assert.Equal(identidad, fila.IdentidadDestinatario);
                return fila;
            }

            var inicial = await Leer();
            Assert.Equal(EstadoAutorizacionEnvio.Preparada, inicial.Estado);
            Assert.Null(inicial.ConsumidaEnUtc);
            Assert.Null(inicial.ResultadoEnUtc);
            output.WriteLine($"Ensayo={ensayo:N}; operación={operacion:N}; estado inicial=Preparada; fechas terminales vacías.");
            Assert.False(Directory.Exists(carpeta));
            Directory.CreateDirectory(carpeta);
            raizCreada = true;
            var instalacion = new InstalacionEnsayo(Guid.NewGuid(), carpeta);
            var valores = new Dictionary<string, string?>
            {
                ["CONSTIA_ENVIO_INSTALACION_ID"] = instalacion.InstalacionId.ToString(),
                ["CONSTIA_ENVIO_DESTINATARIO"] = Destinatario,
                ["CONSTIA_ENVIO_SQL"] = consumidor.ConnectionString,
                ["CONSTIA_ENVIO_HMAC_BASE64"] = Convert.ToBase64String(clave),
                ["Resend__Enabled"] = "true", ["Resend__ApiKey"] = "synthetic-r4b-not-a-secret",
                ["Resend__FromAddress"] = "remitente@example.invalid", ["Resend__RequestTimeoutSeconds"] = "10"
            };
            config = ConfiguracionEnvioReal.Cargar(instalacion, ensayo, operacion, Destinatario, valores.GetValueOrDefault);
            var entrada = EnsayoPreparado.CrearReal(ensayo, operacion, Destinatario, config.ClaveHmac, config.Resend.FromAddress!);
            var handler = new Interceptado(async solicitud =>
            {
                Assert.Equal(HttpMethod.Post, solicitud.Method);
                Assert.Equal(ResendHttpTransport.Endpoint, solicitud.RequestUri!.AbsoluteUri);
                Assert.Equal(operacion.ToString("N"), Assert.Single(solicitud.Headers.GetValues("Idempotency-Key")));
                Assert.Contains(ensayo.ToString("N"), File.ReadAllText(marcador));
                var durante = await Leer();
                Assert.Equal(EstadoAutorizacionEnvio.Consumida, durante.Estado);
                Assert.NotNull(durante.ConsumidaEnUtc);
                Assert.Null(durante.ResultadoEnUtc);
                output.WriteLine("Al entrar al handler: marcador persistido; SQL=Consumida desde otro DbContext.");
            });

            using (var entorno = new EntornoEnvioReal(config, instalacion, handler))
            {
                var resultado = await entorno.Coordinador.EjecutarConResultadoAsync(entrada, config.ClaveHmac, ct);
                // The coordinator catches transport exceptions; rethrow any assertion from the handler.
                if (handler.Error is not null) throw new InvalidOperationException("Comprobación en el handler fallida.", handler.Error);
                Assert.Equal(FinEnsayo.Aceptado, resultado.Fin);
                Assert.Equal(Aceptacion, resultado.Entrega?.IdProveedor);
                Assert.True(resultado.ResultadoRegistrado);
                Assert.Equal(1, handler.Invocaciones);
            }
            var final = await Leer();
            Assert.Equal(EstadoAutorizacionEnvio.Aceptada, final.Estado);
            Assert.NotNull(final.ConsumidaEnUtc);
            Assert.NotNull(final.ResultadoEnUtc);
            Assert.True(final.ResultadoEnUtc >= final.ConsumidaEnUtc);
            var segundoHandler = new Interceptado(_ => throw new InvalidOperationException("El segundo intento llegó al transporte."));
            using (var reinicio = new EntornoEnvioReal(config, instalacion, segundoHandler))
            {
                Assert.Equal(FinEnsayo.InicioRechazado, await reinicio.Coordinador.EjecutarAsync(entrada, config.ClaveHmac, ct));
                Assert.Equal(0, segundoHandler.Invocaciones);
                Assert.False(await reinicio.Autorizacion.ConsumirAsync(ensayo, identidad, operacion, ct));
            }
            var despues = await Leer();
            Assert.Equal(final.Estado, despues.Estado);
            Assert.Equal(final.ConsumidaEnUtc, despues.ConsumidaEnUtc);
            Assert.Equal(final.ResultadoEnUtc, despues.ResultadoEnUtc);
            Assert.Contains(ensayo.ToString("N"), File.ReadAllText(marcador));
            output.WriteLine("Resultado sintético Aceptada persistido; una sola fila; HTTP interceptado=1; segundo inicio rechazado; segundo consumo SQL=false; HTTP adicional=0; marcador conservado.");
        }
        catch (SqlException error)
        {
            throw new InvalidOperationException($"SQL R4B: número={error.Number}, estado={error.State}, clase={error.Class}.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clave);
            if (config is not null) CryptographicOperations.ZeroMemory(config.ClaveHmac);
            if (raizCreada)
            {
                var ruta = Path.GetFullPath(carpeta);
                if (!string.Equals(Path.GetDirectoryName(ruta), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(ruta) != "Constia-C127-ResendR4B-" + token
                    || (File.GetAttributes(ruta) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Ruta temporal no verificable para cleanup.");
                File.Delete(marcador);
                Directory.Delete(ruta);
            }
        }
    }

    private static ConstiaDbContext Contexto(string conexion) =>
        new(new DbContextOptionsBuilder<ConstiaDbContext>().UseSqlServer(conexion, sql => sql.CommandTimeout(30)).Options);

    private static async Task Ejecutar(SqlConnection conexion, string sql, CancellationToken ct)
    {
        await using var comando = new SqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync(ct);
    }

    private sealed class Interceptado(Func<HttpRequestMessage, Task> comprobar) : HttpMessageHandler
    {
        public int Invocaciones { get; private set; }
        public Exception? Error { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
        {
            Invocaciones++;
            try { await comprobar(solicitud); }
            catch (Exception e) { Error = e; throw; }
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"" + Aceptacion + "\"}") };
        }
    }
}

