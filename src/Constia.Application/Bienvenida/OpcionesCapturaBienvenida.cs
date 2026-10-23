namespace Constia.Application.Bienvenida;
public sealed class OpcionesCapturaBienvenida
{
    public bool Habilitada { get; private init; }
    public Guid? Epoca { get; private init; }
    public bool EsValida { get; private init; } = true;
    public static OpcionesCapturaBienvenida DesdeValores(string? habilitada, string? epoca)
    {
        if(habilitada is null) return new();
        if(!bool.TryParse(habilitada,out var activa)) return new(){EsValida=false};
        if(!activa) return new();
        return Guid.TryParse(epoca,out var id) && id!=Guid.Empty
            ? new(){Habilitada=true,Epoca=id}
            : new(){Habilitada=true,EsValida=false};
    }
    public override string ToString() => "[opciones de captura redactadas]";
}
