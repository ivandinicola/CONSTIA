using Constia.Application.Cumplimientos;
using Constia.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Constia.Infrastructure.Cumplimientos;

public sealed class CumplimientoRepository(ConstiaDbContext dbContext) : ICumplimientoRepository
{
    private const string UniqueHabitDateIndexName = "UX_Cumplimiento_HabitoId_Fecha";

    public Task<bool> ExistePorHabitoYFechaAsync(
        Guid habitId,
        DateOnly fecha,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<Cumplimiento>()
            .AsNoTracking()
            .AnyAsync(cumplimiento =>
                EF.Property<Guid>(cumplimiento, "HabitoId") == habitId &&
                cumplimiento.Fecha == fecha,
                cancellationToken);
    }

    public async Task AgregarAsync(Cumplimiento cumplimiento, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cumplimiento);
        dbContext.Set<Cumplimiento>().Add(cumplimiento);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (EsDuplicadoDeHabitoYFecha(exception))
        {
            throw new ConflictoRegistroCumplimientoException();
        }
    }

    private static bool EsDuplicadoDeHabitoYFecha(DbUpdateException exception)
    {
        return exception.InnerException is SqlException sqlException
            && sqlException.Errors.Cast<SqlError>().Any(error =>
                error.Number is 2601 or 2627
                && error.Message.Contains(UniqueHabitDateIndexName, StringComparison.OrdinalIgnoreCase));
    }
}
