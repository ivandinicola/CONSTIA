using System.Text.Json;
using Constia.Application.Bienvenida;
using Constia.Application.Correo;
using Constia.Domain;
namespace Constia.Infrastructure.Bienvenida;
public sealed class PreparadorPayloadBienvenida(PerfilResendBienvenida perfil) : IPreparadorPayloadBienvenida
{
    public string Perfil=>perfil.Huella;
    public byte[] Preparar(EventoBienvenida e)
    {
        if(e.PlantillaVersion!="bienvenida-v1" || !DatosVerificacionCorreo.DireccionSimple(e.Destinatario))
            throw new ArgumentException("Mensaje no elegible.");
        const string asunto="Te damos la bienvenida a CONSTIA";
        const string texto="Hola.\n\nTu correo quedó verificado. Te damos la bienvenida a CONSTIA.\n\nTu constancia, día a día.";
        const string html="<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"></head><body><h1>Te damos la bienvenida a CONSTIA</h1><p>Hola.</p><p>Tu correo quedó verificado.</p><p>Tu constancia, día a día.</p></body></html>";
        var b=JsonSerializer.SerializeToUtf8Bytes(new{from=$"CONSTIA <{perfil.Remitente}>",
            to=new[]{e.Destinatario},subject=asunto,html,text=texto});
        if(b.Length>16384)throw new ArgumentException("Payload fuera de límite.");
        return b;
    }
}
