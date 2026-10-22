namespace Constia.Application.Correo;

public enum FinTrabajoCorreo
{
    Aceptado, NoElegible, PreparacionFallida, FalloFinal, Incierto, Obsoleto, VencidoEnCola,
    Cancelado, FalloInfraestructura, ProveedorNoDisponible, ProveedorNoDisponibleIncierto
}
public sealed class EstadoProcesamientoCorreo
{
    public int Intentos { get; private set; }
    public bool HuboIncertidumbre { get; private set; }
    public bool HuboProveedorNoDisponible { get; private set; }
    public ResultadoEntregaCorreo? Ultimo { get; private set; }
    public void Registrar(ResultadoEntregaCorreo resultado)
    {
        Intentos++;
        Ultimo = resultado;
        HuboIncertidumbre |= resultado.Estado == EstadoEntregaCorreo.ResultadoIncierto;
        HuboProveedorNoDisponible |= resultado.Estado == EstadoEntregaCorreo.ProveedorNoDisponible;
    }
    // La causa de cierre no debe borrar la incertidumbre de un intento previo.
    public FinTrabajoCorreo Final => HuboProveedorNoDisponible
        ? HuboIncertidumbre ? FinTrabajoCorreo.ProveedorNoDisponibleIncierto : FinTrabajoCorreo.ProveedorNoDisponible
        : Ultimo?.Estado == EstadoEntregaCorreo.AceptadaPorProveedor
            ? FinTrabajoCorreo.Aceptado : HuboIncertidumbre ? FinTrabajoCorreo.Incierto : FinTrabajoCorreo.FalloFinal;
}