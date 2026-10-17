using Constia.Domain;

namespace Constia.Infrastructure.Habitos;

public sealed class SnapshotEstadoHabito
{
    public Guid HabitoId { get; private set; }

    public string TimeZoneId { get; private set; } = null!;

    public DateOnly FechaCorteLocal { get; private set; }

    public DateOnly FechaInicio { get; private set; }

    public EstadoHabito EstadoAlCorte { get; private set; }

    private SnapshotEstadoHabito()
    {
    }

    public SnapshotEstadoHabito(
        Guid habitoId,
        string timeZoneId,
        DateOnly fechaCorteLocal,
        DateOnly fechaInicio,
        EstadoHabito estadoAlCorte)
    {
        if (habitoId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del hábito es obligatorio.", nameof(habitoId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        if (!Enum.IsDefined(estadoAlCorte))
        {
            throw new ArgumentOutOfRangeException(nameof(estadoAlCorte));
        }

        HabitoId = habitoId;
        TimeZoneId = timeZoneId;
        FechaCorteLocal = fechaCorteLocal;
        FechaInicio = fechaInicio;
        EstadoAlCorte = estadoAlCorte;
    }
}
