using Microsoft.EntityFrameworkCore;
using Constia.Domain;
using Constia.Infrastructure.Habitos;

namespace Constia.Infrastructure;

public class ConstiaDbContext : DbContext
{
    public ConstiaDbContext(DbContextOptions<ConstiaDbContext> options)
        : base(options)
    {
    }

    public DbSet<Habito> Habitos => Set<Habito>();

    public DbSet<InicializacionEstadoHabito> InicializacionesEstadoHabito => Set<InicializacionEstadoHabito>();

    public DbSet<SnapshotEstadoHabito> SnapshotsEstadoHabito => Set<SnapshotEstadoHabito>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConstiaDbContext).Assembly);
    }
}
