using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Bienvenida;
using Microsoft.EntityFrameworkCore;
namespace Constia.API.Tests;
public sealed class BienvenidaOutboxModelTests
{
    [Fact] public void ModeloC3_ConservaB_YConfiguraUnicidadYRelacionesRestrictivas()
    {
        using var db=new ConstiaDbContext(new DbContextOptionsBuilder<ConstiaDbContext>().UseSqlServer("Server=unused;Database=unused").Options);
        var e=db.Model.FindEntityType(typeof(EventoBienvenida));
        Assert.NotNull(e);
        Assert.Equal("nvarchar(max)", e.FindProperty(nameof(EventoBienvenida.Destinatario))!.GetColumnType());
        Assert.Null(e.FindProperty(nameof(EventoBienvenida.Destinatario))!.GetMaxLength());
        Assert.Contains(e.GetIndexes(),i=>i.IsUnique && i.Properties.Select(p=>p.Name).SequenceEqual(new[]{"UsuarioId"}));
        Assert.All(e.GetForeignKeys(),fk=>Assert.Equal(DeleteBehavior.Restrict,fk.DeleteBehavior));
        Assert.True(e.FindProperty("Version")!.IsConcurrencyToken);
        Assert.NotNull(db.Model.FindEntityType(typeof(IntentoBienvenida)));
        Assert.NotNull(db.Model.FindEntityType(typeof(ControlBienvenida)));
        var user=db.Model.FindEntityType(typeof(Usuario))!;
        Assert.False(user.IsSqlOutputClauseUsed());
        Assert.True(user.FindProperty("VersionCorreo")!.IsConcurrencyToken);
        Assert.Contains(db.Model.FindEntityType(typeof(DesafioVerificacionCorreo))!.GetIndexes(),i=>i.IsUnique && i.GetFilter()=="[ConsumidoEnUtc] IS NULL AND [InvalidadoEnUtc] IS NULL");
    }
}
