namespace Constia.Domain;

public sealed class PeriodoEstadoHabito
{
    public Guid HabitoId { get; private set; }

    public EstadoHabito Estado { get; private set; }

    public DateOnly VigenteDesde { get; private set; }

    public DateOnly? VigenteHasta { get; private set; }

    private PeriodoEstadoHabito()
    {
    }

    public PeriodoEstadoHabito(EstadoHabito estado, DateOnly vigenteDesde, DateOnly? vigenteHasta = null)
    {
        if (!Enum.IsDefined(estado))
        {
            throw new ArgumentOutOfRangeException(nameof(estado));
        }

        if (vigenteHasta is DateOnly fin && fin <= vigenteDesde)
        {
            throw new ArgumentException("La fecha final debe ser posterior a la fecha inicial.", nameof(vigenteHasta));
        }

        Estado = estado;
        VigenteDesde = vigenteDesde;
        VigenteHasta = vigenteHasta;
    }

    public bool AplicaA(DateOnly fecha) =>
        fecha >= VigenteDesde && (VigenteHasta is null || fecha < VigenteHasta.Value);

    internal void CerrarEn(DateOnly fecha)
    {
        if (VigenteHasta is not null)
        {
            throw new InvalidOperationException("No se puede modificar un período histórico cerrado.");
        }

        if (fecha <= VigenteDesde)
        {
            throw new ArgumentException("El período debe tener una duración positiva.", nameof(fecha));
        }

        VigenteHasta = fecha;
    }

    internal void ReemplazarEstadoAbierto(EstadoHabito estado)
    {
        if (VigenteHasta is not null)
        {
            throw new InvalidOperationException("No se puede modificar un período histórico cerrado.");
        }

        if (!Enum.IsDefined(estado))
        {
            throw new ArgumentOutOfRangeException(nameof(estado));
        }

        Estado = estado;
    }

    internal void AsignarHabito(Guid habitoId) => HabitoId = habitoId;
}

public sealed class ConflictoVigenciaEstadoHabitoException(string message) : InvalidOperationException(message);
