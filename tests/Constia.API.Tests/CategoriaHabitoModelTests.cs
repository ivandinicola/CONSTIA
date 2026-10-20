using Constia.Domain;
using Constia.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Constia.API.Tests;

public sealed class CategoriaHabitoModelTests
{
    [Fact]
    public void Categoria_ConfiguraTablaClaveYPropiedadesSinGenerarIdentidad()
    {
        using var context = CrearContexto();
        var categoria = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CategoriaHabito));
        Assert.NotNull(categoria);
        Assert.Equal("CategoriaHabito", categoria.GetTableName());
        Assert.Equal(["Id"], categoria.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(ValueGenerated.Never, categoria.FindProperty("Id")!.ValueGenerated);
        Assert.False(categoria.FindProperty("Nombre")!.IsNullable);
        Assert.False(categoria.FindProperty("Orden")!.IsNullable);
    }

    [Fact]
    public void Categoria_PrecargaExactamenteElCatalogoOficial()
    {
        using var context = CrearContexto();
        var categoria = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CategoriaHabito));
        Assert.NotNull(categoria);
        var seed = categoria.GetSeedData().OrderBy(row => (int)row["Orden"]!).ToArray();
        Assert.Equal(10, seed.Length);
        Assert.Equal(CatalogoCategoriasHabito.Todas.Select(c => (c.Id, c.Nombre, c.Orden)),
            seed.Select(row => ((Guid)row["Id"]!, (string)row["Nombre"]!, (int)row["Orden"]!)));
    }

    [Fact]
    public void Habito_CategoriaEsOpcionalSinDefaultYConEliminacionRestringida()
    {
        using var context = CrearContexto();
        var habito = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Habito))!;
        var property = habito.FindProperty("CategoriaId");
        Assert.NotNull(property);
        Assert.Equal(typeof(Guid?), property.ClrType);
        Assert.True(property.IsNullable);
        Assert.Null(property.GetDefaultValue());
        Assert.Null(property.GetDefaultValueSql());
        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
        var fk = Assert.Single(habito.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(CategoriaHabito));
        Assert.Equal(["CategoriaId"], fk.Properties.Select(p => p.Name));
        Assert.False(fk.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        Assert.Contains(habito.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(["CategoriaId"]));
    }

    [Fact]
    public void Habito_ConstructorActualConservaCategoriaNullYSinEscrituraPublica()
    {
        var property = typeof(Habito).GetProperty("CategoriaId");
        Assert.NotNull(property);
        Assert.False(property.SetMethod!.IsPublic);
        var user = new Usuario("Prueba", "test@example.com", "hash", "Etc/UTC");
        var habit = HabitoHistorico.Crear(user, "Leer", null, new DateOnly(2026, 10, 1), [DayOfWeek.Monday]);
        Assert.Null(property.GetValue(habit));
    }

    internal static ConstiaDbContext CrearContexto() => new(new DbContextOptionsBuilder<ConstiaDbContext>()
        .UseSqlServer("Server=127.0.0.1,1;Database=ModelOnly;Integrated Security=True;TrustServerCertificate=True")
        .Options);
}
