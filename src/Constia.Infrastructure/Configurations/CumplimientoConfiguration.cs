using Constia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Constia.Infrastructure.Configurations;

public class CumplimientoConfiguration : IEntityTypeConfiguration<Cumplimiento>
{
    public void Configure(EntityTypeBuilder<Cumplimiento> builder)
    {
        builder.HasKey(cumplimiento => cumplimiento.Id);

        builder.Property(cumplimiento => cumplimiento.Fecha)
            .IsRequired();

        builder.HasOne(cumplimiento => cumplimiento.Habito)
            .WithMany()
            .HasForeignKey("HabitoId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
