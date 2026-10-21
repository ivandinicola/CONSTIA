namespace Constia.Infrastructure.Usuarios;

// Fila única de serialización: las reservas de la ventana móvil son los desafíos persistidos.
public sealed class PresupuestoEmisionCorreo
{
    public int Id { get; private set; } = 1;
    public long TotalEmisiones { get; private set; }
    public void Reservar() => TotalEmisiones = checked(TotalEmisiones + 1);
}
