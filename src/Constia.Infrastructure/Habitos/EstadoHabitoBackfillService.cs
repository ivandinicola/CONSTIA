using Constia.Application.Habitos;
using Constia.Application.Temporal;
using Constia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace Constia.Infrastructure.Habitos;

public sealed class EstadoHabitoBackfillService(
    ConstiaDbContext dbContext,
    IZonaHorariaIana zonasHorarias,
    TimeProvider timeProvider)
{
    public async Task CapturarSnapshotAsync(
        DateTimeOffset cutoverUtc,
        CancellationToken cancellationToken = default)
    {
        if (cutoverUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("El corte debe ser una fecha y hora UTC.", nameof(cutoverUtc));
        }

        await AsegurarCortePersistidoAsync(cutoverUtc, cancellationToken);
        dbContext.ChangeTracker.Clear();

        var initialization = await dbContext.InicializacionesEstadoHabito
            .SingleAsync(item => item.Id == InicializacionEstadoHabito.RegistroUnicoId, cancellationToken);
        if (initialization.SnapshotCapturedAtUtc is not null)
        {
            await CargarYValidarSnapshotAsync(initialization, cancellationToken);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        // Keep source writes blocked through the durable snapshot commit. The deployment
        // runbook also keeps legacy application instances stopped until this transaction commits.
        await LockTableRowsAsync("Usuario", transaction, cancellationToken);
        await LockTableRowsAsync("Habito", transaction, cancellationToken);

        await dbContext.Entry(initialization).ReloadAsync(cancellationToken);
        if (initialization.SnapshotCapturedAtUtc is not null)
        {
            await CargarYValidarSnapshotAsync(initialization, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var habits = await dbContext.Habitos
            .Include(habit => habit.Usuario)
            .AsNoTracking()
            .OrderBy(habit => habit.Id)
            .ToArrayAsync(cancellationToken);

        var snapshots = new List<SnapshotEstadoHabito>(habits.Length);
        foreach (var habit in habits)
        {
            var timeZone = zonasHorarias.Resolver(habit.Usuario.TimeZoneId);
            var localCutoverDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
                initialization.CutoverUtc, timeZone).DateTime);
            snapshots.Add(new SnapshotEstadoHabito(
                habit.Id,
                habit.Usuario.TimeZoneId,
                localCutoverDate,
                habit.FechaInicio,
                habit.Estado));
        }

        dbContext.SnapshotsEstadoHabito.AddRange(snapshots);
        initialization.MarcarSnapshotCapturado(timeProvider.GetUtcNow(), snapshots.Count);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> AplicarBackfillAsync(CancellationToken cancellationToken = default)
    {
        var initialization = await dbContext.InicializacionesEstadoHabito
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == InicializacionEstadoHabito.RegistroUnicoId, cancellationToken)
            ?? throw new InvalidOperationException("El corte de estados todavía no está persistido.");

        if (initialization.CompletedAtUtc is not null)
        {
            ValidarMetadatosDeFinalizacion(initialization);
            return false;
        }

        if (initialization.SnapshotCapturedAtUtc is null || initialization.ExpectedLegacyHabitCount is null)
        {
            throw new InvalidOperationException("No se puede aplicar el backfill antes de capturar el snapshot.");
        }

        var snapshots = await CargarYValidarSnapshotAsync(initialization, cancellationToken);

        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await BackfillHabitAsync(snapshot, cancellationToken);
        }

        await using var completionTransaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var trackedInitialization = await dbContext.InicializacionesEstadoHabito
            .SingleAsync(item => item.Id == InicializacionEstadoHabito.RegistroUnicoId, cancellationToken);
        await ValidarEstadoCompletoAsync(
            trackedInitialization.ExpectedLegacyHabitCount!.Value, cancellationToken);

        trackedInitialization.MarcarCompletado(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        await completionTransaction.CommitAsync(cancellationToken);
        return true;
    }

    private static void ValidarMetadatosDeFinalizacion(InicializacionEstadoHabito initialization)
    {
        var capturedAtUtc = initialization.SnapshotCapturedAtUtc;
        var completedAtUtc = initialization.CompletedAtUtc;
        if (initialization.CutoverUtc.Offset != TimeSpan.Zero ||
            capturedAtUtc is null || capturedAtUtc.Value.Offset != TimeSpan.Zero ||
            initialization.ExpectedLegacyHabitCount is null or < 0 ||
            completedAtUtc is null || completedAtUtc.Value.Offset != TimeSpan.Zero ||
            capturedAtUtc.Value < initialization.CutoverUtc ||
            completedAtUtc.Value < capturedAtUtc.Value)
        {
            throw new InvalidOperationException("Los metadatos de finalización del historial son inconsistentes.");
        }
    }

    private async Task AsegurarCortePersistidoAsync(DateTimeOffset cutoverUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var initialization = await dbContext.InicializacionesEstadoHabito
            .SingleOrDefaultAsync(item => item.Id == InicializacionEstadoHabito.RegistroUnicoId, cancellationToken);
        if (initialization is null)
        {
            dbContext.InicializacionesEstadoHabito.Add(new InicializacionEstadoHabito(cutoverUtc));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (initialization.CutoverUtc != cutoverUtc)
        {
            throw new InvalidOperationException(
                "El corte UTC ya está fijado; el reintento debe utilizar exactamente el mismo instante.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task BackfillHabitAsync(SnapshotEstadoHabito snapshot, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var habit = await dbContext.Habitos
            .Include(item => item.PeriodosEstado)
            .AsTracking()
            .SingleOrDefaultAsync(item => item.Id == snapshot.HabitoId, cancellationToken)
            ?? throw new InvalidOperationException("Un hábito incluido en el snapshot ya no existe.");

        if (habit.FechaInicio != snapshot.FechaInicio)
        {
            throw new InvalidOperationException(
                "La fecha de inicio actual difiere de la fecha congelada en el snapshot; el backfill no la reconstruirá ni corregirá.");
        }

        var expectedStart = snapshot.FechaInicio > snapshot.FechaCorteLocal
            ? snapshot.FechaInicio
            : snapshot.FechaCorteLocal;
        if (habit.Estado != snapshot.EstadoAlCorte)
        {
            throw new InvalidOperationException(
                "El estado actual difiere del estado congelado en el corte; el backfill no lo reconstruirá ni corregirá.");
        }

        if (habit.PeriodosEstado.Count == 0)
        {
            habit.InicializarHistorialEstadoDesdeCorte(expectedStart, snapshot.EstadoAlCorte);
            await new HabitoRepository(dbContext).GuardarCambiosAsync(habit, cancellationToken);
        }
        else
        {
            var period = habit.PeriodosEstado.SingleOrDefault();
            if (habit.PeriodosEstado.Count != 1 ||
                period is null ||
                period.VigenteDesde != expectedStart ||
                period.VigenteHasta is not null ||
                period.Estado != snapshot.EstadoAlCorte)
            {
                throw new InvalidOperationException("El período parcialmente inicializado no coincide con el snapshot.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
    }

    private async Task<SnapshotEstadoHabito[]> CargarYValidarSnapshotAsync(
        InicializacionEstadoHabito initialization,
        CancellationToken cancellationToken)
    {
        if (initialization.CutoverUtc.Offset != TimeSpan.Zero ||
            initialization.SnapshotCapturedAtUtc?.Offset != TimeSpan.Zero ||
            initialization.ExpectedLegacyHabitCount is not int expectedCount || expectedCount < 0)
        {
            throw new InvalidOperationException("Los metadatos persistidos del corte son inconsistentes.");
        }

        var snapshots = await dbContext.SnapshotsEstadoHabito
            .AsNoTracking()
            .OrderBy(snapshot => snapshot.HabitoId)
            .ToArrayAsync(cancellationToken);
        if (snapshots.Length != expectedCount)
        {
            throw new InvalidOperationException("La cantidad de filas del snapshot no coincide con el corte capturado.");
        }

        foreach (var snapshot in snapshots)
        {
            if (!Enum.IsDefined(snapshot.EstadoAlCorte))
            {
                throw new InvalidOperationException("El snapshot contiene un estado desconocido.");
            }

            var timeZone = zonasHorarias.Resolver(snapshot.TimeZoneId);
            var derivedLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
                initialization.CutoverUtc, timeZone).DateTime);
            if (derivedLocalDate != snapshot.FechaCorteLocal)
            {
                throw new InvalidOperationException(
                    "La fecha local persistida del snapshot no corresponde a su corte UTC y zona horaria congelados.");
            }
        }

        var snapshotIds = snapshots.Select(snapshot => snapshot.HabitoId).ToArray();
        var currentHabits = await dbContext.Habitos
            .AsNoTracking()
            .Where(habit => snapshotIds.Contains(habit.Id))
            .Select(habit => new { habit.Id, habit.FechaInicio, habit.Estado })
            .ToDictionaryAsync(habit => habit.Id, cancellationToken);
        if (currentHabits.Count != snapshots.Length)
        {
            throw new InvalidOperationException("Falta un hábito incluido en el snapshot capturado.");
        }

        foreach (var snapshot in snapshots)
        {
            var current = currentHabits[snapshot.HabitoId];
            if (current.FechaInicio != snapshot.FechaInicio)
            {
                throw new InvalidOperationException(
                    "La fecha de inicio actual difiere de la fecha congelada en el snapshot.");
            }

            // Before completion, status-changing writes are gated. After completion,
            // a legitimate deactivation can make the live projection differ from the cutover.
            if (initialization.CompletedAtUtc is null && current.Estado != snapshot.EstadoAlCorte)
            {
                throw new InvalidOperationException(
                    "El estado actual difiere del estado congelado en el corte; el backfill no lo reconstruirá ni corregirá.");
            }
        }

        return snapshots;
    }

    private async Task ValidarEstadoCompletoAsync(int expectedLegacyHabitCount, CancellationToken cancellationToken)
    {
        var expectedPeriods = await dbContext.SnapshotsEstadoHabito
            .Join(dbContext.Set<PeriodoEstadoHabito>(),
                snapshot => snapshot.HabitoId,
                period => period.HabitoId,
                (snapshot, period) => new { snapshot, period })
            .Where(pair => pair.period.VigenteHasta == null)
            .CountAsync(cancellationToken);
        if (expectedPeriods != expectedLegacyHabitCount)
        {
            throw new InvalidOperationException("No todos los hábitos del snapshot tienen un período vigente.");
        }

        var habitsWithoutOpenState = await dbContext.Habitos.AnyAsync(habit =>
            !dbContext.Set<PeriodoEstadoHabito>().Any(period =>
                period.HabitoId == habit.Id && period.VigenteHasta == null), cancellationToken);
        if (habitsWithoutOpenState)
        {
            throw new InvalidOperationException("Hay hábitos sin un período de estado abierto.");
        }

        var projectionMismatch = await dbContext.Habitos.AnyAsync(habit =>
            !dbContext.Set<PeriodoEstadoHabito>().Any(period =>
                period.HabitoId == habit.Id &&
                period.VigenteHasta == null &&
                period.Estado == habit.Estado), cancellationToken);
        if (projectionMismatch)
        {
            throw new InvalidOperationException("La proyección actual de algún hábito no coincide con su período abierto.");
        }
    }

    private async Task LockTableRowsAsync(
        string tableName,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = dbContext.Database.GetDbConnection().CreateCommand();
        await using (command)
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = $"SELECT [Id] FROM [dbo].[{tableName}] WITH (TABLOCKX, HOLDLOCK);";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
            }
        }
    }
}
