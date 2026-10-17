using Constia.Infrastructure.Habitos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Constia.Infrastructure.Configurations;

public sealed class InicializacionEstadoHabitoConfiguration : IEntityTypeConfiguration<InicializacionEstadoHabito>
{
    public void Configure(EntityTypeBuilder<InicializacionEstadoHabito> builder)
    {
        builder.ToTable("InicializacionEstadoHabito", table =>
        {
            table.HasCheckConstraint("CK_InicializacionEstadoHabito_Singleton", "[Id] = 1");
            table.HasCheckConstraint(
                "CK_InicializacionEstadoHabito_CompletionRequiresSnapshot",
                "[CompletedAtUtc] IS NULL OR [SnapshotCapturedAtUtc] IS NOT NULL");
            table.HasCheckConstraint(
                "CK_InicializacionEstadoHabito_SnapshotCount",
                "([SnapshotCapturedAtUtc] IS NULL AND [ExpectedLegacyHabitCount] IS NULL) OR ([SnapshotCapturedAtUtc] IS NOT NULL AND [ExpectedLegacyHabitCount] IS NOT NULL AND [ExpectedLegacyHabitCount] >= 0)");
        });

        builder.HasKey(initialization => initialization.Id);
        builder.Property(initialization => initialization.Id).ValueGeneratedNever();
        builder.Property(initialization => initialization.CutoverUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(initialization => initialization.SnapshotCapturedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(initialization => initialization.ExpectedLegacyHabitCount);
        builder.Property(initialization => initialization.CompletedAtUtc).HasColumnType("datetimeoffset");
    }
}
