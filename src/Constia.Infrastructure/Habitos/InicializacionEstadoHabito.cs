namespace Constia.Infrastructure.Habitos;

public sealed class InicializacionEstadoHabito
{
    public const int RegistroUnicoId = 1;

    public int Id { get; private set; }

    public DateTimeOffset CutoverUtc { get; private set; }

    public DateTimeOffset? SnapshotCapturedAtUtc { get; private set; }

    public int? ExpectedLegacyHabitCount { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    private InicializacionEstadoHabito()
    {
    }

    public InicializacionEstadoHabito(DateTimeOffset cutoverUtc)
    {
        if (cutoverUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("El corte debe estar expresado en UTC.", nameof(cutoverUtc));
        }

        Id = RegistroUnicoId;
        CutoverUtc = cutoverUtc;
    }

    public void MarcarSnapshotCapturado(DateTimeOffset capturedAtUtc, int expectedLegacyHabitCount)
    {
        if (capturedAtUtc.Offset != TimeSpan.Zero || capturedAtUtc < CutoverUtc)
        {
            throw new ArgumentException("La captura debe ser UTC y no anterior al corte.", nameof(capturedAtUtc));
        }

        if (expectedLegacyHabitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedLegacyHabitCount));
        }

        if (SnapshotCapturedAtUtc is not null && ExpectedLegacyHabitCount != expectedLegacyHabitCount)
        {
            throw new InvalidOperationException("No se puede reemplazar el tamaño del snapshot ya capturado.");
        }

        ExpectedLegacyHabitCount = expectedLegacyHabitCount;
        SnapshotCapturedAtUtc ??= capturedAtUtc;
    }

    public void MarcarCompletado(DateTimeOffset completedAtUtc)
    {
        if (SnapshotCapturedAtUtc is null)
        {
            throw new InvalidOperationException("No se puede completar el backfill antes de capturar el corte.");
        }

        if (completedAtUtc.Offset != TimeSpan.Zero || completedAtUtc < SnapshotCapturedAtUtc)
        {
            throw new ArgumentException("La finalización debe ser UTC y posterior a la captura.", nameof(completedAtUtc));
        }

        CompletedAtUtc ??= completedAtUtc;
    }
}
