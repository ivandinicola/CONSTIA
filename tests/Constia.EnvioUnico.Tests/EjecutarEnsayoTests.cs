using Constia.Application.Correo;
using Constia.Infrastructure.Correo;
using Constia.EnvioUnico;
using System.Diagnostics;
using System.Text.Json;

namespace Constia.EnvioUnico.Tests;

public sealed class EjecutarEnsayoTests : IDisposable
{
    private readonly string carpeta = Path.Combine(Path.GetTempPath(), "Constia-C127-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] clave = Enumerable.Repeat((byte)42, 32).ToArray();
    private readonly Xunit.Abstractions.ITestOutputHelper output;
    public EjecutarEnsayoTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        this.output = output;
        Directory.CreateDirectory(carpeta);
    }
    public void Dispose()
    {
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(carpeta));
        Assert.StartsWith("Constia-C127-", Path.GetFileName(carpeta));
        Directory.Delete(carpeta, recursive: true);
    }
    private EnsayoPreparado Preparar() => EnsayoPreparado.Crear(Guid.NewGuid(), Guid.NewGuid(), "ensayo@example.invalid", clave);
    [Fact]
    public void Coordinador_depende_del_contrato_de_transporte()
    {
        Assert.Contains(typeof(EjecutarEnsayo).GetConstructors(), c =>
            c.GetParameters().Any(p => p.ParameterType == typeof(ITransporteCorreoPreparado)));
    }
    private EjecutarEnsayo Coordinador(IAutorizacionEnsayo auth, TransporteFalso transporte) => new(new RegistroInicioLocal(carpeta), auth, transporte);

    private InstalacionEnsayo Instalacion() => new(Guid.NewGuid(), carpeta);
    private Dictionary<string, string?> Valores(InstalacionEnsayo instalacion) => new()
    {
        ["CONSTIA_ENVIO_INSTALACION_ID"] = instalacion.InstalacionId.ToString(),
        ["CONSTIA_ENVIO_DESTINATARIO"] = "ensayo@example.invalid",
        ["CONSTIA_ENVIO_SQL"] = "Server=sql.invalid;Database=ensayo;Integrated Security=True;Connect Timeout=1",
        ["CONSTIA_ENVIO_HMAC_BASE64"] = Convert.ToBase64String(clave),
        ["Resend__Enabled"] = "true", ["Resend__ApiKey"] = "synthetic-key-not-a-secret",
        ["Resend__FromAddress"] = "correo@example.invalid", ["Resend__FromName"] = "CONSTIA",
        ["Resend__RequestTimeoutSeconds"] = "1"
    };

    [Theory]
    [InlineData("CONSTIA_ENVIO_INSTALACION_ID", "")]
    [InlineData("CONSTIA_ENVIO_DESTINATARIO", "otro@example.invalid")]
    [InlineData("CONSTIA_ENVIO_SQL", "")]
    [InlineData("CONSTIA_ENVIO_SQL", "Server=sql.invalid;Database=master")]
    [InlineData("CONSTIA_ENVIO_HMAC_BASE64", "invalid-base64")]
    [InlineData("CONSTIA_ENVIO_HMAC_BASE64", "AA==")]
    [InlineData("Resend__Enabled", "false")]
    [InlineData("Resend__ApiKey", "")]
    [InlineData("Resend__FromAddress", "invalid")]
    [InlineData("Resend__FromName", "otro")]
    [InlineData("Resend__RequestTimeoutSeconds", "0")]
    [InlineData("Resend__RequestTimeoutSeconds", "31")]
    public void Configuracion_real_invalida_no_admite_inicio(string nombre, string valor)
    {
        var instalacion = Instalacion(); var valores = Valores(instalacion); valores[nombre] = valor;
        Assert.ThrowsAny<ArgumentException>(() => ConfiguracionEnvioReal.Cargar(instalacion,
            Guid.NewGuid(), Guid.NewGuid(), "ensayo@example.invalid", n => valores.GetValueOrDefault(n)));
        Assert.Empty(Directory.GetFiles(carpeta));
    }

    [Fact]
    public async Task Composicion_real_y_payload_con_HTTP_interceptado_sin_SQL()
    {
        var instalacion = Instalacion(); var valores = Valores(instalacion);
        var ensayo = Guid.NewGuid(); var operacion = Guid.NewGuid();
        var config = ConfiguracionEnvioReal.Cargar(instalacion, ensayo, operacion, "ensayo@example.invalid",
            n => valores.GetValueOrDefault(n));
        Assert.Equal(clave, config.ClaveHmac);
        Assert.Equal(0, new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(config.ConexionSql).ConnectRetryCount);
        var entrada = EnsayoPreparado.CrearReal(ensayo, operacion, "ensayo@example.invalid", config.ClaveHmac, config.Resend.FromAddress!);
        var handler = new HttpInterceptado(operacion);
        using var entorno = new EntornoEnvioReal(config, instalacion, handler);
        Assert.IsType<AutorizacionC109>(entorno.Autorizacion);
        Assert.IsType<ResendHttpTransport>(entorno.Transporte);
        // Direct transport characterization only. Never consume a real SQL authorization in this test.
        var resultado = await entorno.Transporte.EnviarAsync(entrada.Solicitud, default);
        Assert.Equal(EstadoEntregaCorreo.AceptadaPorProveedor, resultado.Estado);
        Assert.Equal("provider-id", resultado.IdProveedor);
        Assert.Equal(1, handler.Invocaciones);
        Assert.Empty(Directory.GetFiles(carpeta));
        Assert.Throws<ArgumentException>(() => new EjecutarEnsayo(new RegistroInicioLocal(carpeta),
            new AutorizacionSimulada(entrada), entorno.Transporte, config.Resend.FromAddress));
        Assert.Throws<ArgumentException>(() => new EjecutarEnsayo(new RegistroInicioLocal(carpeta),
            entorno.Autorizacion, new TransporteFalso(), config.Resend.FromAddress));
        Assert.Throws<ArgumentException>(() => new EjecutarEnsayo(new RegistroInicioLocal(carpeta),
            entorno.Autorizacion, entorno.Transporte));
        var alterada = entrada with { Remitente = "otro@example.invalid" };
        Assert.Equal(FinEnsayo.Invalido, await entorno.Coordinador.EjecutarAsync(alterada, clave, default));
        alterada = entrada with { Destinatario = "otro@example.invalid" };
        Assert.Equal(FinEnsayo.Invalido, await entorno.Coordinador.EjecutarAsync(alterada, clave, default));
        alterada = entrada with { OperacionId = Guid.NewGuid() };
        Assert.Equal(FinEnsayo.Invalido, await entorno.Coordinador.EjecutarAsync(alterada, clave, default));
        Assert.Equal(1, handler.Invocaciones);
        Assert.Empty(Directory.GetFiles(carpeta));
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(config.ClaveHmac);
    }

    [Theory]
    [InlineData(EstadoEntregaCorreo.AceptadaPorProveedor, FinEnsayo.Aceptado, EstadoAutorizacionEnvio.Aceptada)]
    [InlineData(EstadoEntregaCorreo.RechazadaDefinitivamente, FinEnsayo.Rechazado, EstadoAutorizacionEnvio.Incierta)]
    [InlineData(EstadoEntregaCorreo.ResultadoIncierto, FinEnsayo.Incierto, EstadoAutorizacionEnvio.Incierta)]
    [InlineData(EstadoEntregaCorreo.CanceladaAntesDelEnvio, FinEnsayo.Cancelado, EstadoAutorizacionEnvio.Incierta)]
    [InlineData(EstadoEntregaCorreo.FalloTemporal, FinEnsayo.Rechazado, EstadoAutorizacionEnvio.Incierta)]
    public async Task Clasificacion_no_se_descarta_ni_reintenta(EstadoEntregaCorreo estado, FinEnsayo esperado, EstadoAutorizacionEnvio terminal)
    {
        var entrega = new ResultadoEntregaCorreo(estado, estado == EstadoEntregaCorreo.ResultadoIncierto ? MotivoEntregaCorreo.Timeout : MotivoEntregaCorreo.Ninguno,
            estado == EstadoEntregaCorreo.AceptadaPorProveedor ? "provider-id" : null,
            intentoHttpIniciado: estado != EstadoEntregaCorreo.CanceladaAntesDelEnvio);
        var transporte = new TransporteResultado(entrega); var auth = new AutorizacionPrueba();
        var coordinador = new EjecutarEnsayo(new RegistroInicioLocal(carpeta), auth, transporte);
        var entrada = Preparar();
        var resultado = await coordinador.EjecutarConResultadoAsync(entrada, clave, default);
        Assert.Equal(esperado, resultado.Fin); Assert.Same(entrega, resultado.Entrega);
        Assert.True(resultado.ResultadoRegistrado); Assert.Equal(terminal, auth.Terminal);
        Assert.Equal(1, transporte.Invocaciones); Assert.Equal(1, auth.Resultados);
        Assert.Equal(FinEnsayo.InicioRechazado, await coordinador.EjecutarAsync(entrada, clave, default));
        Assert.Equal(1, transporte.Invocaciones); Assert.Equal(1, auth.Consumos);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Aceptacion_con_registro_fallido_se_conserva_sin_reenvio(bool lanzar)
    {
        var entrega = new ResultadoEntregaCorreo(EstadoEntregaCorreo.AceptadaPorProveedor, MotivoEntregaCorreo.Ninguno,
            "accepted-id", 200, intentoHttpIniciado: true);
        var transporte = new TransporteResultado(entrega);
        var auth = new AutorizacionPrueba { Registrar = false, LanzarRegistro = lanzar };
        var resultado = await new EjecutarEnsayo(new RegistroInicioLocal(carpeta), auth, transporte)
            .EjecutarConResultadoAsync(Preparar(), clave, default);
        Assert.Equal(FinEnsayo.RegistroFallido, resultado.Fin);
        Assert.Equal(EstadoEntregaCorreo.AceptadaPorProveedor, resultado.Entrega!.Estado);
        Assert.Equal("accepted-id", resultado.Entrega.IdProveedor);
        Assert.False(resultado.ResultadoRegistrado); Assert.Equal(1, transporte.Invocaciones);
        Assert.Equal(EstadoAutorizacionEnvio.Aceptada, auth.Terminal);
        Assert.Single(Directory.GetFiles(carpeta));
    }

    private sealed class TransporteResultado(ResultadoEntregaCorreo resultado) : ITransporteCorreoPreparado
    {
        public int Invocaciones;
        public Task<ResultadoEntregaCorreo> EnviarAsync(SolicitudCorreoPreparada solicitud, CancellationToken ct)
        { Invocaciones++; return Task.FromResult(resultado); }
    }

    private sealed class HttpInterceptado(Guid operacion) : HttpMessageHandler
    {
        public int Invocaciones;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Invocaciones++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.resend.com/emails", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("synthetic-key-not-a-secret", request.Headers.Authorization.Parameter);
            Assert.Equal(operacion.ToString("N"), Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            using var json = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(ct));
            var root = json.RootElement;
            Assert.Equal(new[] { "from", "to", "subject", "text" }, root.EnumerateObject().Select(x => x.Name).ToArray());
            Assert.Equal("CONSTIA <correo@example.invalid>", root.GetProperty("from").GetString());
            Assert.Equal("ensayo@example.invalid", Assert.Single(root.GetProperty("to").EnumerateArray()).GetString());
            Assert.Equal("CONSTIA — ensayo de envío único", root.GetProperty("subject").GetString());
            Assert.Equal($"Correo técnico de ensayo único de CONSTIA. Operación: {operacion:N}.", root.GetProperty("text").GetString());
            return new(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"provider-id\"}") };
        }
    }



    [Theory]
    [InlineData("ensayo")]
    [InlineData("operacion")]
    [InlineData("destinatario")]
    [InlineData("tipo")]
    [InlineData("payload")]
    [InlineData("identidad")]
    public async Task Entrada_invalida_no_consume_ni_invoca(string campo)
    {
        var original = Preparar();
        var entrada = campo switch
        {
            "ensayo" => original with { EnsayoId = Guid.Empty },
            "operacion" => original with { OperacionId = Guid.Empty },
            "destinatario" => original with { Destinatario = "otro@example.invalid" },
            "tipo" => original with { Tipo = "bienvenida" },
            "payload" => original with { Solicitud = new(original.Solicitud.ClaveIdempotencia, [1, 2, 3]) },
            _ => original with { Identidad = new byte[32] }
        };
        var auth = new AutorizacionPrueba(); var transporte = new TransporteFalso();
        Assert.Equal(FinEnsayo.Invalido, await Coordinador(auth, transporte).EjecutarAsync(entrada, clave, default));
        Assert.Equal(0, auth.Consumos); Assert.Equal(0, transporte.Invocaciones);
        Assert.Empty(Directory.GetFiles(carpeta));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Consumo_false_o_excepcion_no_concede_transporte(bool permitido, bool lanzar)
    {
        var auth = new AutorizacionPrueba { Permitido = permitido, Lanzar = lanzar };
        var transporte = new TransporteFalso(); var entrada = Preparar();
        Assert.Equal(FinEnsayo.NoAutorizado, await Coordinador(auth, transporte).EjecutarAsync(entrada, clave, default));
        Assert.Equal(1, auth.Consumos); Assert.Equal(0, transporte.Invocaciones);
        Assert.True(File.Exists(Path.Combine(carpeta, entrada.EnsayoId.ToString("N") + ".inicio")));
    }

    [Fact]
    public async Task Consumo_true_invoca_una_vez_y_registra()
    {
        var auth = new AutorizacionPrueba(); var transporte = new TransporteFalso();
        auth.AntesDeConsumir = () => Assert.Single(Directory.GetFiles(carpeta));
        Assert.Equal(FinEnsayo.Simulado, await Coordinador(auth, transporte).EjecutarAsync(Preparar(), clave, default));
        Assert.Equal(1, auth.Consumos); Assert.Equal(1, transporte.Invocaciones); Assert.Equal(1, auth.Resultados);
    }

    [Fact]
    public async Task Cancelacion_despues_del_consumo_no_invoca()
    {
        using var ct = new CancellationTokenSource();
        var auth = new AutorizacionPrueba { AntesDeConsumir = ct.Cancel }; var transporte = new TransporteFalso();
        Assert.Equal(FinEnsayo.Cancelado, await Coordinador(auth, transporte).EjecutarAsync(Preparar(), clave, ct.Token));
        Assert.Equal(1, auth.Consumos); Assert.Equal(0, transporte.Invocaciones);
    }

    [Fact]
    public async Task Cancelacion_inicial_no_consume_ni_invoca()
    {
        var auth = new AutorizacionPrueba(); var transporte = new TransporteFalso();
        Assert.Equal(FinEnsayo.Cancelado, await Coordinador(auth, transporte).EjecutarAsync(Preparar(), clave, new CancellationToken(true)));
        Assert.Equal(0, auth.Consumos); Assert.Equal(0, transporte.Invocaciones);
    }

    [Theory]
    [InlineData(ModoTransporteFalso.Timeout)]
    [InlineData(ModoTransporteFalso.Error)]
    public async Task Fallo_de_transporte_no_reintenta(ModoTransporteFalso modo)
    {
        var auth = new AutorizacionPrueba(); var transporte = new TransporteFalso(modo);
        Assert.Equal(FinEnsayo.Incierto, await Coordinador(auth, transporte).EjecutarAsync(Preparar(), clave, default));
        Assert.Equal(1, transporte.Invocaciones); Assert.Equal(1, auth.Resultados);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fallo_al_registrar_no_reenvia(bool lanzar)
    {
        var auth = new AutorizacionPrueba { Registrar = false, LanzarRegistro = lanzar }; var transporte = new TransporteFalso();
        Assert.Equal(FinEnsayo.RegistroFallido, await Coordinador(auth, transporte).EjecutarAsync(Preparar(), clave, default));
        Assert.Equal(1, transporte.Invocaciones); Assert.Equal(1, auth.Resultados);
    }

    [Fact]
    public async Task Segundo_intento_reinicio_y_SQL_restaurado_no_reutilizan_identificador()
    {
        var entrada = Preparar(); var primero = new TransporteFalso();
        Assert.Equal(FinEnsayo.Simulado, await Coordinador(new AutorizacionPrueba(), primero).EjecutarAsync(entrada, clave, default));
        // A fresh authorization double simulates SQL restored to Preparada; new objects simulate restart.
        for (var intento = 0; intento < 3; intento++)
        {
            var restaurada = new AutorizacionPrueba(); var siguiente = new TransporteFalso();
            Assert.Equal(FinEnsayo.InicioRechazado, await Coordinador(restaurada, siguiente).EjecutarAsync(entrada, clave, default));
            Assert.Equal(0, restaurada.Consumos); Assert.Equal(0, siguiente.Invocaciones);
        }
        Assert.Equal(1, primero.Invocaciones);
    }

    [Fact]
    public async Task Dos_ejecuciones_concurrentes_solo_una_inicia()
    {
        var entrada = Preparar(); var a = new TransporteFalso(); var b = new TransporteFalso();
        using var barrera = new Barrier(2);
        Task<FinEnsayo> Iniciar(TransporteFalso t) => Task.Run(async () =>
        {
            Assert.True(barrera.SignalAndWait(TimeSpan.FromSeconds(10)));
            return await Coordinador(new AutorizacionPrueba(), t).EjecutarAsync(entrada, clave, default);
        });
        var resultados = await Task.WhenAll(Iniciar(a), Iniciar(b));
        Assert.Single(resultados, x => x == FinEnsayo.Simulado);
        Assert.Single(resultados, x => x == FinEnsayo.InicioRechazado);
        Assert.Equal(1, a.Invocaciones + b.Invocaciones);
        Assert.Single(Directory.GetFiles(carpeta));
    }

    [Fact]
    public async Task Registro_parcial_preexistente_impide_reinicio()
    {
        var entrada = Preparar(); File.WriteAllBytes(Path.Combine(carpeta, entrada.EnsayoId.ToString("N") + ".inicio"), []);
        var auth = new AutorizacionPrueba(); var transporte = new TransporteFalso();
        Assert.Equal(FinEnsayo.InicioRechazado, await Coordinador(auth, transporte).EjecutarAsync(entrada, clave, default));
        Assert.Equal(0, auth.Consumos); Assert.Equal(0, transporte.Invocaciones);
    }

    [Fact]
    public void Payload_es_copia_inmutable()
    {
        var entrada = Preparar(); var antes = entrada.Solicitud.CopiarPayload();
        var copia = entrada.Solicitud.CopiarPayload(); copia[0] ^= 1;
        Assert.Equal(antes, entrada.Solicitud.CopiarPayload());
    }

    [Fact]
    public void Registro_rechaza_directorio_fuera_del_entorno_de_ensayo()
    {
        Assert.Throws<ArgumentException>(() => new RegistroInicioLocal(Path.GetTempPath()));
        Assert.Throws<ArgumentException>(() => new RegistroInicioLocal(Path.Combine(carpeta, "Constia-C127-inexistente")));
    }

    [Fact]
    public async Task Error_de_registro_no_consume_ni_invoca()
    {
        var entrada = Preparar();
        Directory.CreateDirectory(Path.Combine(carpeta, entrada.EnsayoId.ToString("N") + ".inicio"));
        var auth = new AutorizacionPrueba(); var transporte = new TransporteFalso();
        Assert.Equal(FinEnsayo.InicioRechazado, await Coordinador(auth, transporte).EjecutarAsync(entrada, clave, default));
        Assert.Equal(0, auth.Consumos); Assert.Equal(0, transporte.Invocaciones);
    }

    [Fact]
    public async Task Autorizacion_simulada_exige_identidad_y_no_repite_consumo()
    {
        var entrada = Preparar(); var auth = new AutorizacionSimulada(entrada);
        Assert.False(await auth.ConsumirAsync(entrada.EnsayoId, new byte[32], entrada.OperacionId, default));
        Assert.False(await auth.ConsumirAsync(Guid.NewGuid(), entrada.Identidad, entrada.OperacionId, default));
        Assert.False(await auth.ConsumirAsync(entrada.EnsayoId, entrada.Identidad, Guid.NewGuid(), default));
        Assert.True(await auth.ConsumirAsync(entrada.EnsayoId, entrada.Identidad, entrada.OperacionId, default));
        Assert.False(await auth.ConsumirAsync(entrada.EnsayoId, entrada.Identidad, entrada.OperacionId, default));
    }

    [Fact]
    public async Task Transporte_cuenta_entrada_cancelada_sin_completar()
    {
        var transporte = new TransporteFalso();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transporte.EnviarAsync(Preparar().Solicitud, new CancellationToken(true)));
        Assert.Equal(1, transporte.Invocaciones);
    }

    [Theory]
    [InlineData(@"\\servidor\recurso\Constia-C127-prueba")]
    [InlineData(@"\\?\C:\Constia-C127-prueba")]
    [InlineData("Constia-C127-relativa")]
    public void Registro_rechaza_UNC_dispositivos_y_rutas_relativas(string ruta) =>
        Assert.Throws<ArgumentException>(() => new RegistroInicioLocal(ruta));

    [Fact]
    public void Registro_rechaza_normalizacion_silenciosa()
    {
        var alias = Path.Combine(carpeta, "..", Path.GetFileName(carpeta));
        Assert.Throws<ArgumentException>(() => new RegistroInicioLocal(alias));
    }

    [Fact(Skip = "No se crea ni accede a una unidad de red real en esta suite sin red; rechazo por DriveType.Network revisado estaticamente.")]
    public void Unidad_remota_real_no_acreditada() => throw new NotSupportedException();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registro_rechaza_junction_en_raiz_o_antecesor(bool antecesor)
    {
        var destino = Path.Combine(carpeta, "destino");
        Directory.CreateDirectory(destino);
        var enlace = Path.Combine(carpeta, "Constia-C127-enlace");
        var ruta = enlace;
        if (antecesor)
        {
            Directory.CreateDirectory(Path.Combine(destino, "Constia-C127-hija"));
            ruta = Path.Combine(enlace, "Constia-C127-hija");
        }
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", enlace, destino }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
        try { Assert.Throws<ArgumentException>(() => new RegistroInicioLocal(ruta)); }
        finally { Directory.Delete(enlace); }
    }

    [Fact]
    public async Task Dos_procesos_reales_comparten_raiz_y_no_admiten_raiz_CLI()
    {
        var instalacion = Path.Combine(carpeta, "instalacion");
        Directory.CreateDirectory(instalacion);
        foreach (var archivo in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories))
        {
            var copia = Path.Combine(instalacion, Path.GetRelativePath(AppContext.BaseDirectory, archivo));
            Directory.CreateDirectory(Path.GetDirectoryName(copia)!);
            File.Copy(archivo, copia);
        }
        var identidad = Guid.NewGuid();
        File.WriteAllText(Path.Combine(instalacion, InstalacionEnsayo.Archivo), JsonSerializer.Serialize(new InstalacionEnsayo(identidad, carpeta)));
        var entrada = Preparar();
        var procesos = new ProcesosPrueba(TimeSpan.FromSeconds(20));
        Process Iniciar(params string[] args)
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "exec", "--runtimeconfig", Path.Combine(instalacion, "Constia.EnvioUnico.Tests.runtimeconfig.json"), "--depsfile", Path.Combine(instalacion, "Constia.EnvioUnico.Tests.deps.json"), Path.Combine(instalacion, "Constia.EnvioUnico.dll") }.Concat(args)) start.ArgumentList.Add(arg);
            return procesos.Iniciar(start);
        }
        try
        {
            var a = Iniciar(entrada.EnsayoId.ToString(), entrada.OperacionId.ToString(), entrada.Destinatario);
            var b = Iniciar(entrada.EnsayoId.ToString(), entrada.OperacionId.ToString(), entrada.Destinatario);
            var listo = await Task.WhenAll(a.StandardOutput.ReadLineAsync(procesos.Token).AsTask(), b.StandardOutput.ReadLineAsync(procesos.Token).AsTask());
            Assert.All(listo, texto => Assert.StartsWith($"LISTO instalacion={identidad:N}", texto));
            await Task.WhenAll(a.StandardInput.WriteLineAsync("INICIAR".AsMemory(), procesos.Token), b.StandardInput.WriteLineAsync("INICIAR".AsMemory(), procesos.Token));
            var resultados = await Task.WhenAll(procesos.LeerHastaFin(a), procesos.LeerHastaFin(b));
            var salidas = resultados.Select(x => x.Salida).ToArray();
            var errores = resultados.Select(x => x.Error).ToArray();
            Assert.All(errores, texto => Assert.Empty(texto));
            Assert.Equal(new[] { 0, 1 }, new[] { a.ExitCode, b.ExitCode }.Order().ToArray());
            Assert.Single(salidas, texto => texto.Contains("resultado=Simulado; invocaciones_falsas=1"));
            Assert.Single(salidas, texto => texto.Contains("resultado=InicioRechazado; invocaciones_falsas=0"));
            var archivo = Assert.Single(Directory.GetFiles(carpeta, "*.inicio"));
            Assert.Contains(entrada.EnsayoId.ToString("N"), File.ReadAllText(archivo));
            output.WriteLine($"Procesos independientes: A={a.Id} exit={a.ExitCode}; B={b.Id} exit={b.ExitCode}; ambos LISTO antes de INICIAR; registro unico persistido.");

            var alternativa = Path.Combine(carpeta, "Constia-C127-alternativa");
            Directory.CreateDirectory(alternativa);
            var invalido = Iniciar(alternativa, entrada.EnsayoId.ToString(), entrada.OperacionId.ToString(), entrada.Destinatario);
            var invalidOutput = await procesos.LeerHastaFin(invalido);
            Assert.Equal(2, invalido.ExitCode);
            Assert.DoesNotContain("LISTO", invalidOutput.Salida);
            Assert.Empty(Directory.GetFiles(alternativa));
            Assert.Single(Directory.GetFiles(carpeta, "*.inicio"));
        }
        finally
        {
            await procesos.DisposeAsync();
            output.WriteLine($"Cleanup confirmado para {procesos.Finalizados.Count} procesos propios; duracion total={procesos.Duracion.TotalSeconds:F2}s.");
        }
    }

    [Fact]
    public async Task Proceso_bloqueado_agota_plazo_y_se_limpia()
    {
        var procesos = new ProcesosPrueba(TimeSpan.FromSeconds(2));
        var pid = 0;
        try
        {
            var hijo = procesos.Iniciar(InicioBloqueado());
            pid = hijo.Id;
            Assert.Equal("LISTO", (await hijo.StandardOutput.ReadLineAsync(procesos.Token))?.Trim());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => procesos.LeerHastaFin(hijo));
        }
        finally { await procesos.DisposeAsync(); }
        Assert.Contains(pid, procesos.Finalizados);
        Assert.True(procesos.Duracion < TimeSpan.FromSeconds(7));
        output.WriteLine($"Proceso bloqueado {pid}: timeout observado y terminacion confirmada en {procesos.Duracion.TotalSeconds:F2}s.");
    }

    [Fact]
    public async Task Segundo_arranque_fallido_limpia_el_primero()
    {
        var procesos = new ProcesosPrueba(TimeSpan.FromSeconds(10));
        var pid = 0;
        try
        {
            var primero = procesos.Iniciar(InicioBloqueado());
            pid = primero.Id;
            Assert.Equal("LISTO", (await primero.StandardOutput.ReadLineAsync(procesos.Token))?.Trim());
            var inexistente = new ProcessStartInfo(Path.Combine(carpeta, "no-existe.exe")) { UseShellExecute = false };
            Assert.Throws<System.ComponentModel.Win32Exception>(() => procesos.Iniciar(inexistente));
        }
        finally { await procesos.DisposeAsync(); }
        Assert.Contains(pid, procesos.Finalizados);
        output.WriteLine($"Segundo arranque rechazado; primer hijo {pid} terminado y liberado.");
    }

    private static ProcessStartInfo InicioBloqueado()
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "/d", "/c", "echo LISTO & set /p entrada=" }) start.ArgumentList.Add(arg);
        return start;
    }

    private sealed class ProcesosPrueba(TimeSpan trabajo) : IAsyncDisposable
    {
        private readonly List<Process> propios = [];
        private readonly CancellationTokenSource plazo = new(trabajo);
        private readonly Stopwatch reloj = Stopwatch.StartNew();
        public List<int> Finalizados { get; } = [];
        public CancellationToken Token => plazo.Token;
        public TimeSpan Duracion => reloj.Elapsed;

        public Process Iniciar(ProcessStartInfo start)
        {
            Token.ThrowIfCancellationRequested();
            var process = new Process { StartInfo = start };
            try
            {
                if (!process.Start()) throw new InvalidOperationException("No se inicio el proceso propio.");
                propios.Add(process);
                return process;
            }
            catch { process.Dispose(); throw; }
        }

        public async Task<(string Salida, string Error)> LeerHastaFin(Process process)
        {
            var salida = process.StandardOutput.ReadToEndAsync(Token);
            var error = process.StandardError.ReadToEndAsync(Token);
            await Task.WhenAll(salida, error, process.WaitForExitAsync(Token)).WaitAsync(Token);
            return (await salida, await error);
        }

        public async ValueTask DisposeAsync()
        {
            var errores = new List<Exception>();
            try
            {
                foreach (var process in propios)
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                    catch (Exception ex) { errores.Add(ex); }
                }
                // One total budget: work deadline plus at most five seconds reserved for cleanup.
                var restante = trabajo + TimeSpan.FromSeconds(5) - reloj.Elapsed;
                if (restante > TimeSpan.FromSeconds(5)) restante = TimeSpan.FromSeconds(5);
                try
                {
                    await Task.WhenAll(propios.Select(p => p.WaitForExitAsync()))
                        .WaitAsync(restante > TimeSpan.Zero ? restante : TimeSpan.FromMilliseconds(1));
                    foreach (var process in propios)
                    {
                        Assert.True(process.HasExited);
                        Finalizados.Add(process.Id);
                    }
                }
                catch (Exception ex) { errores.Add(ex); }
            }
            finally
            {
                foreach (var process in propios)
                {
                    foreach (var stream in new IDisposable[] { process.StandardInput, process.StandardOutput, process.StandardError })
                    {
                        try { stream.Dispose(); }
                        catch (Exception ex) { errores.Add(ex); }
                    }
                    process.Dispose();
                }
                plazo.Dispose();
                reloj.Stop();
            }
            if (errores.Count != 0) throw new AggregateException("Cleanup de procesos propios incompleto.", errores);
        }
    }

    private sealed class AutorizacionPrueba : IAutorizacionEnsayo
    {
        public bool Permitido = true, Lanzar, Registrar = true, LanzarRegistro;
        public int Consumos, Resultados;
        public EstadoAutorizacionEnvio? Terminal;
        public Action? AntesDeConsumir;
        public Task<bool> ConsumirAsync(Guid ensayo, byte[] identidad, Guid operacion, CancellationToken ct)
        {
            Consumos++; AntesDeConsumir?.Invoke();
            if (Lanzar) throw new InvalidOperationException("Simulado");
            return Task.FromResult(Permitido);
        }
        public Task<bool> RegistrarAsync(Guid ensayo, Guid operacion, EstadoAutorizacionEnvio resultado, CancellationToken ct)
        {
            Resultados++; Terminal = resultado;
            if (LanzarRegistro) throw new InvalidOperationException("Simulado");
            return Task.FromResult(Registrar);
        }
    }
}
