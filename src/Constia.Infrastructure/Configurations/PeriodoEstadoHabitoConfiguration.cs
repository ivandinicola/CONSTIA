using Constia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Constia.Infrastructure.Configurations;

public sealed class PeriodoEstadoHabitoConfiguration : IEntityTypeConfiguration<PeriodoEstadoHabito>
{
    public void Configure(EntityTypeBuilder<PeriodoEstadoHabito> builder)
    {
        builder.ToTable("PeriodoEstadoHabito", table =>
        {
            table.HasCheckConstraint("CK_PeriodoEstadoHabito_Estado", "[Estado] IN (0, 1)");
            table.HasCheckConstraint(
                "CK_PeriodoEstadoHabito_Vigencia",
                "[VigenteHasta] IS NULL OR [VigenteHasta] > [VigenteDesde]");
        });

        builder.HasKey(nameof(PeriodoEstadoHabito.HabitoId), nameof(PeriodoEstadoHabito.VigenteDesde));
        builder.Property(periodo => periodo.HabitoId).IsRequired();
        builder.Property(periodo => periodo.Estado).HasConversion<int>().IsRequired();
        builder.Property(periodo => periodo.VigenteDesde).HasColumnType("date").IsRequired();
        builder.Property(periodo => periodo.VigenteHasta).HasColumnType("date");

        builder.HasIndex(periodo => periodo.HabitoId)
            .IsUnique()
            .HasFilter("[VigenteHasta] IS NULL");

        builder.HasOne<Habito>()
            .WithMany(habito => habito.PeriodosEstado)
            .HasForeignKey(periodo => periodo.HabitoId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
