using Constia.Domain;
using Constia.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Constia.API.Tests;

public sealed class CumplimientoUniqueIndexModelTests
{
    [Fact]
    public void Modelo_DebeImpedirMasDeUnCumplimientoPorHabitoYFecha()
    {
        var options = new DbContextOptionsBuilder<ConstiaDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=ModelOnly;User Id=fake;Password=NotARealPassword!2026;TrustServerCertificate=True")
            .Options;
        using var context = new ConstiaDbContext(options);

        var entity = context.Model.FindEntityType(typeof(Cumplimiento));
        Assert.NotNull(entity);
        var index = Assert.Single(entity.GetIndexes(), candidate =>
            candidate.IsUnique &&
            candidate.Properties.Select(property => property.Name)
                .SequenceEqual(["HabitoId", nameof(Cumplimiento.Fecha)]));

        Assert.Equal("UX_Cumplimiento_HabitoId_Fecha", index.GetDatabaseName());
    }
}
