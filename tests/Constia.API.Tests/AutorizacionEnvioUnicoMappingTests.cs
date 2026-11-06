using Constia.Infrastructure;
using Constia.Infrastructure.Correo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Text.RegularExpressions;

namespace Constia.API.Tests;

public sealed class AutorizacionEnvioUnicoMappingTests
{
    private static ConstiaDbContext Contexto() => new(new DbContextOptionsBuilder<ConstiaDbContext>()
        .UseSqlServer("Server=127.0.0.1,1;Database=NoConnection;User Id=none;Password=none;TrustServerCertificate=True;Connect Timeout=1").Options);

    [Fact]
    public void Modelo_no_contiene_correo_en_claro_y_ensayo_y_operacion_son_unicos()
    {
        using var db = Contexto(); var t = db.Model.FindEntityType(typeof(AutorizacionEnvioUnico))!;
        Assert.Equal("AutorizacionEnvioUnico", t.GetTableName());
        Assert.Equal(nameof(AutorizacionEnvioUnico.EnsayoId), Assert.Single(t.FindPrimaryKey()!.Properties).Name);
        Assert.Contains(t.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(AutorizacionEnvioUnico.OperacionId));
        Assert.DoesNotContain(t.GetProperties(), p => p.Name.Contains("Email", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("binary(32)", t.FindProperty("_identidadDestinatario")!.GetColumnType());
    }
    [Fact]
    public void Migracion_contiene_solo_tabla_nueva_y_down_protege_historial()
    {
        using var db = Contexto(); var migrator = db.GetService<IMigrator>();
        var up = migrator.GenerateScript("20261023003711_AddWelcomeEmailOutbox", "20261030232834_AddAutorizacionEnvioUnico");
        Assert.Contains("CREATE TABLE [AutorizacionEnvioUnico]", up);
        Assert.Contains("CREATE UNIQUE INDEX [IX_AutorizacionEnvioUnico_OperacionId]", up);
        Assert.DoesNotContain("ALTER TABLE [Usuario]", up);
        var down = migrator.GenerateScript("20261030232834_AddAutorizacionEnvioUnico", "20261023003711_AddWelcomeEmailOutbox")
            .ReplaceLineEndings("\n");
        var migration = db.GetService<IMigrationsAssembly>().CreateMigration(
            db.GetService<IMigrationsAssembly>().Migrations["20261030232834_AddAutorizacionEnvioUnico"], db.Database.ProviderName!);
        Assert.Equal(2, migration.DownOperations.Count);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.False(guard.SuppressTransaction);
        Assert.Equal("AutorizacionEnvioUnico", Assert.IsType<DropTableOperation>(migration.DownOperations[1]).Name);
        var contrato = Regex.Match(guard.Sql,
            @"\A\s*IF\s+@@TRANCOUNT\s*=\s*0\s+OR\s+XACT_STATE\s*\(\s*\)\s*<>\s*1\s+" +
            @"THROW\s+51001\s*,\s*'Authorization rollback requires an active committable transaction\.'\s*,\s*1\s*;\s*" +
            @"IF\s+EXISTS\s*\(\s*SELECT\s+1\s+FROM\s+\[AutorizacionEnvioUnico\]\s+" +
            @"WITH\s*\(\s*TABLOCKX\s*,\s*HOLDLOCK\s*\)\s*\)\s+" +
            @"THROW\s+51000\s*,\s*'Cannot discard authorization history\.'\s*,\s*1\s*;\s*\z");
        Assert.True(contrato.Success, guard.Sql);
        var guardSql = guard.Sql.ReplaceLineEndings("\n");
        var guardIndex = down.IndexOf(guardSql, StringComparison.Ordinal);
        Assert.True(guardIndex >= 0);
        Assert.True(down.IndexOf("DROP TABLE [AutorizacionEnvioUnico]", StringComparison.Ordinal) > guardIndex + guardSql.Length);
        Assert.Contains("DROP TABLE [AutorizacionEnvioUnico]", down);
    }
    [Fact]
    public async Task Transaccion_ambiental_no_autoriza_consumo_ni_resultado()
    {
        await using var db = Contexto(); var repo = new AutorizacionEnvioUnicoRepository(db, TimeProvider.System);
        using var scope = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
        Assert.False(await repo.ConsumirAsync(Guid.NewGuid(), new byte[32], Guid.NewGuid(), default));
        Assert.False(await repo.RegistrarResultadoAsync(Guid.NewGuid(), Guid.NewGuid(), EstadoAutorizacionEnvio.Aceptada, default));
    }
    [Fact]
    public async Task Sin_proveedor_falla_cerrado()
    {
        await using var db = new ConstiaDbContext(new DbContextOptionsBuilder<ConstiaDbContext>().Options);
        var repo = new AutorizacionEnvioUnicoRepository(db, TimeProvider.System);
        Assert.False(await repo.ConsumirAsync(Guid.NewGuid(), new byte[32], Guid.NewGuid(), default));
        Assert.False(await repo.RegistrarResultadoAsync(Guid.NewGuid(), Guid.NewGuid(), EstadoAutorizacionEnvio.Aceptada, default));
    }
    [Fact]
    public async Task Conexion_inaccesible_y_argumentos_invalidos_fallan_cerrado()
    {
        await using var db = Contexto(); var repo = new AutorizacionEnvioUnicoRepository(db, TimeProvider.System);
        Assert.False(await repo.ConsumirAsync(Guid.Empty, new byte[32], Guid.NewGuid(), default));
        Assert.False(await repo.ConsumirAsync(Guid.NewGuid(), new byte[31], Guid.NewGuid(), default));
        Assert.False(await repo.ConsumirAsync(Guid.NewGuid(), new byte[32], Guid.NewGuid(), default));
        Assert.False(await repo.RegistrarResultadoAsync(Guid.NewGuid(), Guid.NewGuid(), EstadoAutorizacionEnvio.Preparada, default));
    }
}
