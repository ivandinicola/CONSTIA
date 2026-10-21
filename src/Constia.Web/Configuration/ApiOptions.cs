namespace Constia.Web.Configuration;

/// <summary>Configuración del cliente HTTP; el shell no requiere una API disponible.</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";
    public const string ClientName = "ConstiaApi";

    public string? BaseUrl { get; set; }
}
