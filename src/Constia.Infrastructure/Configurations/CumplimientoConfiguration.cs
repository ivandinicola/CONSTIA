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

        builder.HasIndex("HabitoId", nameof(Cumplimiento.Fecha))
            .IsUnique()
            .HasDatabaseName("UX_Cumplimiento_HabitoId_Fecha");

        builder.HasOne(cumplimiento => cumplimiento.Habito)
            .WithMany()
            .HasForeignKey("HabitoId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
