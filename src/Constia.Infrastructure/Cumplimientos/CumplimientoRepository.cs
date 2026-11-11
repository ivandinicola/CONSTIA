using Constia.Application.Cumplimientos;
using Constia.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Constia.Infrastructure.Cumplimientos;

public sealed class CumplimientoRepository(ConstiaDbContext dbContext) : ICumplimientoRepository, IHistorialCumplimientosRepository
{
    private const string UniqueHabitDateIndexName = "UX_Cumplimiento_HabitoId_Fecha";

    public async Task<IReadOnlyList<CumplimientoListado>?> ListarPorHabitoYUsuarioAsync(
        Guid habitoId, Guid usuarioId, CancellationToken cancellationToken)
    {
        var filas = await (
            from habito in dbContext.Set<Habito>()
            where habito.Id == habitoId && EF.Property<Guid>(habito, "UsuarioId") == usuarioId
            join cumplimiento in dbContext.Set<Cumplimiento>()
                on habito.Id equals EF.Property<Guid>(cumplimiento, "HabitoId") into cumplimientos
            from cumplimiento in cumplimientos.DefaultIfEmpty()
            orderby cumplimiento.Fecha descending
            select new { Id = (Guid?)cumplimiento.Id, Fecha = (DateOnly?)cumplimiento.Fecha })
            .ToListAsync(cancellationToken);

        // El LEFT JOIN conserva una fila sin cumplimiento si existe el hábito propio.
        if (filas.Count == 0)
        {
            return null;
        }

        return filas[0].Id is null
            ? []
            : filas.Select(fila => new CumplimientoListado(fila.Id!.Value, fila.Fecha!.Value)).ToArray();
    }

    public async Task<IReadOnlySet<DateOnly>> ListarFechasAsync(
        Guid habitoId, Guid usuarioId, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken)
    {
        var fechas = await dbContext.Set<Cumplimiento>()
            .Where(c => EF.Property<Guid>(c, "HabitoId") == habitoId
                && EF.Property<Guid>(c.Habito, "UsuarioId") == usuarioId
                && c.Fecha >= desde && c.Fecha <= hasta)
            .Select(c => c.Fecha)
            .ToListAsync(cancellationToken);
        return fechas.ToHashSet();
    }

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
