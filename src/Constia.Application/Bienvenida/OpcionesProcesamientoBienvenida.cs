namespace Constia.Application.Bienvenida;
public sealed class OpcionesProcesamientoBienvenida
{
    public bool ProcesamientoHabilitado { get; private init; }
    public bool EnvioHabilitado { get; private init; }
    public bool MantenimientoHabilitado { get; private init; }
    public bool EsValida { get; private init; } = true;
    public PerfilResendBienvenida? Perfil { get; private init; }
    public static OpcionesProcesamientoBienvenida DesdeValores(string? procesar, string? enviar, string? mantener,
        string? cuenta, string? remitente)
    {
        bool Parse(string? s, out bool b) { b=false; return s is null || bool.TryParse(s,out b); }
        if (!Parse(procesar,out var p) || !Parse(enviar,out var e) || !Parse(mantener,out var m))
            return new(){EsValida=false};
        PerfilResendBienvenida? perfil=null;
        if (p || e)
        {
            try { perfil = new(cuenta!,remitente!); }
            catch (ArgumentException) { return new(){EsValida=false}; }
        }
        return new(){ProcesamientoHabilitado=p,EnvioHabilitado=e,MantenimientoHabilitado=m,Perfil=perfil};
    }
    public override string ToString() => "[opciones de bienvenida redactadas]";
}
