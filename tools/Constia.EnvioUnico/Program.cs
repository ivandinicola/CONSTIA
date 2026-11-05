using System.Security.Cryptography;
using Constia.EnvioUnico;

var real = args.Length == 4 && args[0] == "--real";
var parametros = real ? args[1..] : args;
if (parametros.Length != 3 || !Guid.TryParse(parametros[0], out var ensayo) || ensayo == Guid.Empty
    || !Guid.TryParse(parametros[1], out var operacion) || operacion == Guid.Empty)
{
    Console.Error.WriteLine("Uso: [--real] <ensayo-guid> <operacion-guid> <destinatario>. Sin --real solo se simula.");
    return 2;
}
byte[] clave = [];
try
{
    var instalacion = InstalacionEnsayo.Cargar();
    if (real)
    {
        var config = ConfiguracionEnvioReal.Cargar(instalacion, ensayo, operacion, parametros[2], Environment.GetEnvironmentVariable);
        clave = config.ClaveHmac;
        var entrada = EnsayoPreparado.CrearReal(ensayo, operacion, parametros[2], clave, config.Resend.FromAddress!);
        using var entorno = new EntornoEnvioReal(config, instalacion);
        Console.WriteLine($"LISTO REAL instalacion={instalacion.InstalacionId:N}; ensayo={ensayo:N}; operacion={operacion:N}; escriba ENVIAR-UNA-VEZ.");
        if (await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)) != "ENVIAR-UNA-VEZ") return 2;
        var resultado = await entorno.Coordinador.EjecutarConResultadoAsync(entrada, clave, CancellationToken.None);
        Console.WriteLine($"ensayo={ensayo:N}; operacion={operacion:N}; resultado={resultado.Fin}; estado={resultado.Entrega?.Estado}; motivo={resultado.Entrega?.Motivo}; http={resultado.Entrega?.HttpStatus}; id_proveedor={resultado.Entrega?.IdProveedor}; resultado_registrado={resultado.ResultadoRegistrado}; aceptacion_no_acredita_entrega=True");
        return resultado.Fin == FinEnsayo.Aceptado && resultado.ResultadoRegistrado ? 0 : 1;
    }
    else
    {
        clave = RandomNumberGenerator.GetBytes(32);
        var entrada = EnsayoPreparado.Crear(ensayo, operacion, parametros[2], clave);
        var transporte = new TransporteFalso();
        var coordinador = new EjecutarEnsayo(new RegistroInicioLocal(instalacion.Raiz), new AutorizacionSimulada(entrada), transporte);
        Console.WriteLine($"LISTO instalacion={instalacion.InstalacionId:N}; escriba INICIAR para el ensayo simulado.");
        if (await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)) != "INICIAR") return 2;
        var resultado = await coordinador.EjecutarAsync(entrada, clave, CancellationToken.None);
        Console.WriteLine($"SIMULACION SIN SQL NI HTTP; ensayo={ensayo:N}; operacion={operacion:N}; resultado={resultado}; invocaciones_falsas={transporte.Invocaciones}");
        return resultado == FinEnsayo.Simulado ? 0 : 1;
    }
}
catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
{
    Console.Error.WriteLine("Ensayo interrumpido. Sin reintentos ni borrado de registros. Si el inicio fue admitido, conservarlo y reconciliar antes de cualquier otro ensayo.");
    return 2;
}
finally { CryptographicOperations.ZeroMemory(clave); }
