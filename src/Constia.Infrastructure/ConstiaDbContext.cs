using Microsoft.EntityFrameworkCore;

namespace Constia.Infrastructure;

public class ConstiaDbContext : DbContext
{
    public ConstiaDbContext(DbContextOptions<ConstiaDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConstiaDbContext).Assembly);
    }
}
