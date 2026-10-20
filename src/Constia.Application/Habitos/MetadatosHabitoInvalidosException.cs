namespace Constia.Application.Habitos;

/// <summary>Error de un campo de metadatos informado; no depende del transporte HTTP.</summary>
public sealed class MetadatosHabitoInvalidosException(string campo, string mensaje) : Exception(mensaje)
{
    public string Campo { get; } = campo;
}
