using Constia.Infrastructure;
using Constia.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
namespace Constia.API.Tests;

public sealed class EmailVerificationMigrationTests
{
    [Fact]
    public void Up_ClasificaHistoricosBajoBloqueoYProtegeAltas()
    {
        var ops = new AddEmailVerification().UpOperations;
        var sql = string.Join("\n", ops.OfType<SqlOperation>().Select(x => x.Sql));
        Assert.Contains("TABLOCKX", sql);
        Assert.Contains("SET [EstadoVerificacionCorreo] = 0", sql);
        Assert.Contains("CREATE TRIGGER", sql);
        Assert.Contains("THROW 51031", sql);
        var col = Assert.Single(ops.OfType<AddColumnOperation>(), x => x.Name == "EstadoVerificacionCorreo");
        Assert.Equal((byte)1, col.DefaultValue);
    }

    [Fact]
    public void Down_RechazaAntesDeBorrarConCodigoNuevo()
    {
        var ops = new AddEmailVerification().DownOperations;
        var guard = Assert.IsType<SqlOperation>(ops[0]);
        Assert.Contains("THROW 51030", guard.Sql);
        Assert.Contains("[EstadoVerificacionCorreo] <> 0", guard.Sql);
        Assert.Contains("[DesafioVerificacionCorreo]", guard.Sql);
    }

    [Fact]
    public void Modelo_TriggerSinOutputUnicidadVigenteYRowversion()
    {
        using var db = new ConstiaDbContext(new DbContextOptionsBuilder<ConstiaDbContext>().UseSqlServer("Server=unused;Database=unused").Options);
        var entity = db.Model.FindEntityType(typeof(Constia.Domain.DesafioVerificacionCorreo))!;
        Assert.True(entity.FindProperty("Version")!.IsConcurrencyToken);
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.GetFilter() == "[ConsumidoEnUtc] IS NULL AND [InvalidadoEnUtc] IS NULL");
        var usuario = db.Model.FindEntityType(typeof(Constia.Domain.Usuario))!;
        Assert.False(usuario.IsSqlOutputClauseUsed());
    }
}
