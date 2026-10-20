using Constia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Constia.API.Tests;

public sealed class HabitoIconoMigrationTests
{
    [Fact]
    public void Modelo_IconoEsOpcionalAcotadoSinDefaultFkNiIndiceNuevo()
    {
        using var context = CategoriaHabitoModelTests.CrearContexto();
        var entity = context.Model.FindEntityType(typeof(Habito))!;
        var icono = entity.FindProperty("IconoId");
        Assert.NotNull(icono);
        Assert.True(icono.IsNullable);
        Assert.Equal(64, icono.GetMaxLength());
        Assert.Equal("nvarchar(64)", icono.GetColumnType());
        Assert.Null(icono.GetDefaultValue());
        Assert.Null(icono.GetDefaultValueSql());
        Assert.DoesNotContain(entity.GetIndexes(), i => i.Properties.Contains(icono));
        Assert.DoesNotContain(entity.GetForeignKeys(), fk => fk.Properties.Contains(icono));
        Assert.Null(context.Model.FindEntityType(typeof(IconoHabito)));
    }

    [Fact]
    public void Up_AgregaUnicamenteIconoNullableYSinDefault()
    {
        using var context = CategoriaHabitoModelTests.CrearContexto();
        var assembly = context.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.Where(m => m.Key.EndsWith("_AddHabitIconId")).ToArray();
        Assert.Single(entry);
        var migration = assembly.CreateMigration(entry[0].Value, context.Database.ProviderName!);
        var column = Assert.IsType<AddColumnOperation>(Assert.Single(migration.UpOperations));
        Assert.Equal("Habito", column.Table);
        Assert.Equal("IconoId", column.Name);
        Assert.Equal(typeof(string), column.ClrType);
        Assert.True(column.IsNullable);
        Assert.Equal("nvarchar(64)", column.ColumnType);
        Assert.Equal(64, column.MaxLength);
        Assert.Null(column.DefaultValue);
        Assert.Null(column.DefaultValueSql);
    }

    [Fact]
    public void Down_BloqueaAsignacionesAntesDeEliminarColumnaEnLaMismaTransaccion()
    {
        using var context = CategoriaHabitoModelTests.CrearContexto();
        var assembly = context.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.Where(m => m.Key.EndsWith("_AddHabitIconId")).ToArray();
        Assert.Single(entry);
        var migration = assembly.CreateMigration(entry[0].Value, context.Database.ProviderName!);
        Assert.Equal(2, migration.DownOperations.Count);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.False(guard.SuppressTransaction);
        Assert.Contains("WITH (TABLOCKX, HOLDLOCK)", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[IconoId] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51021", guard.Sql, StringComparison.Ordinal);
        var drop = Assert.IsType<DropColumnOperation>(migration.DownOperations[1]);
        Assert.Equal("Habito", drop.Table);
        Assert.Equal("IconoId", drop.Name);
    }
}
