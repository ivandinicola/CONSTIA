using Constia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Constia.API.Tests;

public sealed class CategoriaHabitoMigrationTests
{
    [Fact]
    public void Up_ContieneSoloLaTablaCategoriaColumnaNullableIndiceFkYDiezInserciones()
    {
        using var context = CategoriaHabitoModelTests.CrearContexto();
        var migrations = context.GetService<IMigrationsAssembly>();
        var type = migrations.Migrations.Single(m => m.Key.EndsWith("_AddHabitCategories")).Value;
        var migration = migrations.CreateMigration(type, context.Database.ProviderName!);
        var operations = migration.UpOperations;
        Assert.Equal(5, operations.Count);
        var column = Assert.Single(operations.OfType<AddColumnOperation>());
        Assert.Equal("Habito", column.Table);
        Assert.Equal("CategoriaId", column.Name);
        Assert.True(column.IsNullable);
        Assert.Null(column.DefaultValue);
        Assert.Null(column.DefaultValueSql);
        Assert.Equal("CategoriaHabito", Assert.Single(operations.OfType<CreateTableOperation>()).Name);
        var seed = Assert.Single(operations.OfType<InsertDataOperation>());
        Assert.Equal("CategoriaHabito", seed.Table);
        Assert.Equal(["Id", "Nombre", "Orden"], seed.Columns);
        Assert.Equal(10, seed.Values.GetLength(0));
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(CatalogoCategoriasHabito.Todas[i].Id, seed.Values[i, 0]);
            Assert.Equal(CatalogoCategoriasHabito.Todas[i].Nombre, seed.Values[i, 1]);
            Assert.Equal(CatalogoCategoriasHabito.Todas[i].Orden, seed.Values[i, 2]);
        }
        var index = Assert.Single(operations.OfType<CreateIndexOperation>());
        Assert.Equal("Habito", index.Table);
        Assert.Equal(["CategoriaId"], index.Columns);
        var fk = Assert.Single(operations.OfType<AddForeignKeyOperation>());
        Assert.Equal("Habito", fk.Table);
        Assert.Equal("CategoriaHabito", fk.PrincipalTable);
        Assert.Equal(["CategoriaId"], fk.Columns);
        Assert.Equal(ReferentialAction.Restrict, fk.OnDelete);
        Assert.Equal(5, migration.DownOperations.Count);
        Assert.Equal("Habito", Assert.Single(migration.DownOperations.OfType<DropForeignKeyOperation>()).Table);
        Assert.Equal("CategoriaHabito", Assert.Single(migration.DownOperations.OfType<DropTableOperation>()).Name);
        Assert.Equal("IX_Habito_CategoriaId", Assert.Single(migration.DownOperations.OfType<DropIndexOperation>()).Name);
        Assert.Equal("CategoriaId", Assert.Single(migration.DownOperations.OfType<DropColumnOperation>()).Name);
    }

    [Fact]
    public void Down_VerificaAsignacionesAntesDeEliminarObjetosYSinSuprimirTransaccion()
    {
        using var context = CategoriaHabitoModelTests.CrearContexto();
        var migrations = context.GetService<IMigrationsAssembly>();
        var type = migrations.Migrations.Single(m => m.Key.EndsWith("_AddHabitCategories")).Value;
        var migration = migrations.CreateMigration(type, context.Database.ProviderName!);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.False(guard.SuppressTransaction);
        Assert.Equal("""
            IF EXISTS (SELECT 1 FROM [Habito] WITH (TABLOCKX, HOLDLOCK) WHERE [CategoriaId] IS NOT NULL)
            BEGIN
                THROW 51020, N'CAT-02: no se puede revertir mientras existan hábitos con categoría asignada. Revise las asignaciones antes del rollback.', 1;
            END;
            """, guard.Sql);
        Assert.IsType<DropForeignKeyOperation>(migration.DownOperations[1]);
        Assert.IsType<DropTableOperation>(migration.DownOperations[2]);
        Assert.IsType<DropIndexOperation>(migration.DownOperations[3]);
        Assert.IsType<DropColumnOperation>(migration.DownOperations[4]);
    }

    [Fact]
    public void Snapshot_CoincideConModeloActualSinCambiosPendientes()
    {
        using var context = CategoriaHabitoModelTests.CrearContexto();
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
