using Microsoft.EntityFrameworkCore;

namespace Constia.Infrastructure;

public class ConstiaDbContext : DbContext
{
    public ConstiaDbContext(DbContextOptions<ConstiaDbContext> options)
        : base(options)
    {
    }
}
