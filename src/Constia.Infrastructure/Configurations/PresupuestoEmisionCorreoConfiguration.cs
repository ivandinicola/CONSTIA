using Constia.Infrastructure.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Constia.Infrastructure.Configurations;
public sealed class PresupuestoEmisionCorreoConfiguration : IEntityTypeConfiguration<PresupuestoEmisionCorreo>
{
    public void Configure(EntityTypeBuilder<PresupuestoEmisionCorreo> b)
    {
        b.ToTable("PresupuestoEmisionCorreo", t => t.HasCheckConstraint("CK_PresupuestoCorreo", "[Id] = 1 AND [TotalEmisiones] >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property<byte[]>("Version").IsRowVersion();
        b.HasData(new { Id = 1, TotalEmisiones = 0L });
    }
}
