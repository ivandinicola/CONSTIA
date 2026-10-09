using Constia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Constia.Infrastructure.Configurations;

public sealed class ProgramacionHabitoConfiguration : IEntityTypeConfiguration<ProgramacionHabito>
{
    public void Configure(EntityTypeBuilder<ProgramacionHabito> builder)
    {
        builder.ToTable("ProgramacionHabito", table => table.HasCheckConstraint(
            "CK_ProgramacionHabito_Vigencia",
            "[VigenteHasta] IS NULL OR [VigenteHasta] > [VigenteDesde]"));

        builder.HasKey("HabitoId", nameof(ProgramacionHabito.VigenteDesde));

        builder.Property<Guid>("HabitoId")
            .IsRequired();

        builder.Property(programacion => programacion.VigenteDesde)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(programacion => programacion.VigenteHasta)
            .HasColumnType("date");

        builder.HasIndex("HabitoId")
            .IsUnique()
            .HasFilter("[VigenteHasta] IS NULL");

        builder.OwnsMany<DiaProgramado>(nameof(ProgramacionHabito.DiasProgramados), dias =>
        {
            dias.ToTable("ProgramacionHabitoDia");

            dias.WithOwner()
                .HasForeignKey("HabitoId", nameof(ProgramacionHabito.VigenteDesde));

            dias.Property<Guid>("HabitoId")
                .IsRequired();

            dias.Property<DateOnly>(nameof(ProgramacionHabito.VigenteDesde))
                .HasColumnType("date")
                .IsRequired();

            dias.Property(dia => dia.Dia)
                .HasConversion<int>()
                .IsRequired();

            dias.HasKey(
                "HabitoId",
                nameof(ProgramacionHabito.VigenteDesde),
                nameof(DiaProgramado.Dia));
        });

        builder.Navigation(programacion => programacion.DiasProgramados)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
