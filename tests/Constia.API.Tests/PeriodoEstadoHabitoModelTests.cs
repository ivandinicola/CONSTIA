using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Habitos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Constia.API.Tests;

public sealed class PeriodoEstadoHabitoModelTests
{
    [Fact]
    public void Modelo_ConfiguraPeriodosYRestriccionesDeVigencia()
    {
        using var context = CrearContexto();
        var model = context.GetService<IDesignTimeModel>().Model;
        var period = model.FindEntityType(typeof(PeriodoEstadoHabito));
        Assert.NotNull(period);

        Assert.Equal("PeriodoEstadoHabito", period.GetTableName());
        Assert.Equal(["HabitoId", nameof(PeriodoEstadoHabito.VigenteDesde)],
            period.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal("date", period.FindProperty(nameof(PeriodoEstadoHabito.VigenteDesde))!.GetColumnType());
        Assert.Equal("date", period.FindProperty(nameof(PeriodoEstadoHabito.VigenteHasta))!.GetColumnType());
        var checks = period.GetCheckConstraints().OrderBy(check => check.Name).ToArray();
        Assert.Collection(checks,
            check =>
            {
                Assert.Equal("CK_PeriodoEstadoHabito_Estado", check.Name);
                Assert.Equal("[Estado] IN (0, 1)", check.Sql);
            },
            check =>
            {
                Assert.Equal("CK_PeriodoEstadoHabito_Vigencia", check.Name);
                Assert.Equal("[VigenteHasta] IS NULL OR [VigenteHasta] > [VigenteDesde]", check.Sql);
            });
        var uniqueOpen = Assert.Single(period.GetIndexes());
        Assert.True(uniqueOpen.IsUnique);
        Assert.Equal(["HabitoId"], uniqueOpen.Properties.Select(property => property.Name));
        Assert.Equal("[VigenteHasta] IS NULL", uniqueOpen.GetFilter());
    }

    [Fact]
    public void Modelo_ConfiguraCorteInmutableSnapshotYMarcadoresSinDefaultsDePreparacion()
    {
        using var context = CrearContexto();
        var model = context.GetService<IDesignTimeModel>().Model;
        var initialization = model.FindEntityType(typeof(InicializacionEstadoHabito));
        var snapshot = model.FindEntityType(typeof(SnapshotEstadoHabito));

        Assert.NotNull(initialization);
        Assert.NotNull(snapshot);
        Assert.Equal("InicializacionEstadoHabito", initialization.GetTableName());
        Assert.Equal("SnapshotEstadoHabito", snapshot.GetTableName());
        Assert.Equal(ValueGenerated.Never, initialization.FindProperty(nameof(InicializacionEstadoHabito.Id))!.ValueGenerated);
        Assert.False(initialization.FindProperty(nameof(InicializacionEstadoHabito.CutoverUtc))!.IsNullable);
        Assert.True(initialization.FindProperty(nameof(InicializacionEstadoHabito.SnapshotCapturedAtUtc))!.IsNullable);
        Assert.True(initialization.FindProperty(nameof(InicializacionEstadoHabito.CompletedAtUtc))!.IsNullable);
        Assert.True(initialization.FindProperty(nameof(InicializacionEstadoHabito.ExpectedLegacyHabitCount))!.IsNullable);
        Assert.Null(initialization.FindProperty(nameof(InicializacionEstadoHabito.CompletedAtUtc))!.GetDefaultValueSql());
        Assert.Null(snapshot.FindProperty(nameof(SnapshotEstadoHabito.EstadoAlCorte))!.GetDefaultValueSql());
        Assert.Equal(ValueGenerated.Never,
            snapshot.FindProperty(nameof(SnapshotEstadoHabito.EstadoAlCorte))!.ValueGenerated);
        Assert.Equal(["HabitoId"], snapshot.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal("date", snapshot.FindProperty(nameof(SnapshotEstadoHabito.FechaCorteLocal))!.GetColumnType());
    }

    [Fact]
    public void Modelo_ConservaElNombreHistoricoDeLaTablaHabito()
    {
        using var context = CrearContexto();
        var habit = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Habito));

        Assert.NotNull(habit);
        Assert.Equal("Habito", habit.GetTableName());
    }

    [Fact]
    public void Inicializacion_NoPermiteFinalizarAntesDelSnapshotYConservaElCorteAlReintentar()
    {
        var cutover = new DateTimeOffset(2026, 10, 16, 2, 30, 0, TimeSpan.Zero);
        var initialization = new InicializacionEstadoHabito(cutover);

        Assert.Throws<InvalidOperationException>(() => initialization.MarcarCompletado(cutover));
        initialization.MarcarSnapshotCapturado(cutover.AddMinutes(1), 0);
        initialization.MarcarSnapshotCapturado(cutover.AddMinutes(2), 0);
        initialization.MarcarCompletado(cutover.AddMinutes(3));

        Assert.Equal(cutover, initialization.CutoverUtc);
        Assert.Equal(cutover.AddMinutes(1), initialization.SnapshotCapturedAtUtc);
        Assert.Equal(cutover.AddMinutes(3), initialization.CompletedAtUtc);
        Assert.Equal(0, initialization.ExpectedLegacyHabitCount);
        Assert.Throws<InvalidOperationException>(() =>
            initialization.MarcarSnapshotCapturado(cutover.AddMinutes(4), 1));
    }

    [Fact]
    public void AgregarHabitoAlContexto_IncluyePeriodoInicialActivo()
    {
        using var context = CrearContexto();
        var user = new Usuario("Ana", "ana@example.com", "hashed-value", "Etc/UTC");
        var start = new DateOnly(2026, 10, 1);
        var habit = new Habito(user, "Leer", null, start, [DayOfWeek.Monday]);
        context.Add(habit);

        var entry = Assert.Single(context.ChangeTracker.Entries<PeriodoEstadoHabito>());
        Assert.Equal(EntityState.Added, entry.State);
        Assert.Equal(habit.Id, entry.Property<Guid>(nameof(PeriodoEstadoHabito.HabitoId)).CurrentValue);
        Assert.Equal(EstadoHabito.Activo, entry.Property<EstadoHabito>(nameof(PeriodoEstadoHabito.Estado)).CurrentValue);
        Assert.Equal(start, entry.Property<DateOnly>(nameof(PeriodoEstadoHabito.VigenteDesde)).CurrentValue);
    }

    private static ConstiaDbContext CrearContexto() => new(new DbContextOptionsBuilder<ConstiaDbContext>()
        .UseSqlServer("Server=127.0.0.1,1;Database=ModelOnly;User Id=fake;Password=FakeOnly!2026;TrustServerCertificate=True")
        .Options);
}
