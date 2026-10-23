using Constia.Domain;
namespace Constia.Application.Bienvenida;
public interface IPreparadorPayloadBienvenida
{
    string Perfil { get; }
    byte[] Preparar(EventoBienvenida evento);
}
