using Constia.Domain;
using Constia.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Constia.API.Tests;

public sealed class ProgramacionHabitoModelTests
{
    [Fact]
    public void ModeloEf_ConfiguraVersionesDiasYRestriccionesEsperadas()
    {
        using var dbContext = CrearDbContext();
        var modelo = dbContext.GetService<IDesignTimeModel>().Model;
        var habito = modelo.FindEntityType(typeof(Habito));
        var programacion = modelo.FindEntityType(typeof(ProgramacionHabito));
        var dias = Assert.Single(modelo.GetEntityTypes(), tipo => tipo.ClrType == typeof(DiaProgramado));

        Assert.NotNull(habito);
        Assert.NotNull(programacion);
        Assert.Null(habito.FindNavigation(nameof(Habito.DiasProgramados)));
        Assert.Equal("ProgramacionHabito", programacion.GetTableName());
        Assert.Equal(
            ["HabitoId", nameof(ProgramacionHabito.VigenteDesde)],
            programacion.FindPrimaryKey()!.Properties.Select(propiedad => propiedad.Name));
        Assert.Equal("date", programacion.FindProperty(nameof(ProgramacionHabito.VigenteDesde))!.GetColumnType());
        Assert.Equal("date", programacion.FindProperty(nameof(ProgramacionHabito.VigenteHasta))!.GetColumnType());

        var relacionHabito = Assert.Single(programacion.GetForeignKeys());
        Assert.Equal(typeof(Habito), relacionHabito.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, relacionHabito.DeleteBehavior);
        Assert.Equal(["HabitoId"], relacionHabito.Properties.Select(propiedad => propiedad.Name));

        var indiceVersionAbierta = Assert.Single(programacion.GetIndexes());
        Assert.Equal(["HabitoId"], indiceVersionAbierta.Properties.Select(propiedad => propiedad.Name));
        Assert.True(indiceVersionAbierta.IsUnique);
        Assert.Equal("[VigenteHasta] IS NULL", indiceVersionAbierta.GetFilter());

        var checkVigencia = Assert.Single(programacion.GetCheckConstraints());
        Assert.Equal("CK_ProgramacionHabito_Vigencia", checkVigencia.Name);
        Assert.Equal(
            "[VigenteHasta] IS NULL OR [VigenteHasta] > [VigenteDesde]",
            checkVigencia.Sql);

        Assert.Equal("ProgramacionHabitoDia", dias.GetTableName());
        Assert.Equal(
            ["HabitoId", nameof(ProgramacionHabito.VigenteDesde), nameof(DiaProgramado.Dia)],
            dias.FindPrimaryKey()!.Properties.Select(propiedad => propiedad.Name));
        var relacionDias = Assert.Single(dias.GetForeignKeys());
        Assert.Same(programacion, relacionDias.PrincipalEntityType);
        Assert.Equal(DeleteBehavior.Cascade, relacionDias.DeleteBehavior);
        Assert.Equal(
            ["HabitoId", nameof(ProgramacionHabito.VigenteDesde)],
            relacionDias.Properties.Select(propiedad => propiedad.Name));
    }

    [Fact]
    public void AgregarHabitoAlContexto_IncluyeLaVersionInicialYSusDiasEnElGrafo()
    {
        using var dbContext = CrearDbContext();
        var usuario = new Usuario("Ana", "ana@example.com", "hashed-value", "Etc/UTC");
        var fechaInicio = new DateOnly(2026, 10, 5);
        var habito = new Habito(
            usuario,
            "Leer",
            null,
            fechaInicio,
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);

        dbContext.Add(habito);

        var entradaProgramacion = Assert.Single(dbContext.ChangeTracker.Entries<ProgramacionHabito>());
        Assert.Equal(EntityState.Added, entradaProgramacion.State);
        Assert.Equal(habito.Id, entradaProgramacion.Property<Guid>("HabitoId").CurrentValue);
        Assert.Equal(fechaInicio, entradaProgramacion.Property<DateOnly>(nameof(ProgramacionHabito.VigenteDesde)).CurrentValue);
        Assert.Equal(2, dbContext.ChangeTracker.Entries<DiaProgramado>().Count());
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], habito.DiasProgramados.Select(dia => dia.Dia));
    }

    private static ConstiaDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<ConstiaDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ConstiaModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new ConstiaDbContext(options);
    }
}
