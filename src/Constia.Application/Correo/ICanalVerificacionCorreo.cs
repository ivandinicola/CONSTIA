namespace Constia.Application.Correo;

public interface ICanalVerificacionCorreo
{
    bool Disponible { get; }
    IReservaTrabajoCorreo? Reservar();
    void Iniciar();
    void CerrarAdmision();
    void Fallar();
    Task EsperarVacioAsync(CancellationToken ct);
    ValueTask<TrabajoVerificacionCorreo?> LeerAsync(CancellationToken ct);
}
public interface ICanalCorreoDePrueba : ICanalVerificacionCorreo { }
public interface IReservaTrabajoCorreo : IDisposable
{
    // Publicar transfiere la propiedad. Dispose del productor no libera después de transferir.
    void Publicar(TrabajoVerificacionCorreo trabajo);
}
