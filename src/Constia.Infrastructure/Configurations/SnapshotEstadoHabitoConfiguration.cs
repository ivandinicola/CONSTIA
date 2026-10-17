using Constia.Domain;
using Constia.Infrastructure.Habitos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Constia.Infrastructure.Configurations;

public sealed class SnapshotEstadoHabitoConfiguration : IEntityTypeConfiguration<SnapshotEstadoHabito>
{
    public void Configure(EntityTypeBuilder<SnapshotEstadoHabito> builder)
    {
        builder.ToTable("SnapshotEstadoHabito", table => table.HasCheckConstraint(
            "CK_SnapshotEstadoHabito_Estado", "[EstadoAlCorte] IN (0, 1)"));

        builder.HasKey(snapshot => snapshot.HabitoId);
        builder.Property(snapshot => snapshot.HabitoId).IsRequired();
        builder.Property(snapshot => snapshot.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(snapshot => snapshot.FechaCorteLocal).HasColumnType("date").IsRequired();
        builder.Property(snapshot => snapshot.FechaInicio).HasColumnType("date").IsRequired();
        builder.Property(snapshot => snapshot.EstadoAlCorte).HasConversion<int>().IsRequired();
        builder.HasOne<Habito>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.HabitoId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
