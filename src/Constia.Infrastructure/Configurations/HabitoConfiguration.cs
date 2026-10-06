using Constia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Constia.Infrastructure.Configurations;

public class HabitoConfiguration : IEntityTypeConfiguration<Habito>
{
    public void Configure(EntityTypeBuilder<Habito> builder)
    {
        builder.HasKey(habito => habito.Id);

        builder.Property(habito => habito.Nombre)
            .IsRequired();

        builder.Property(habito => habito.Descripcion);

        builder.Property(habito => habito.FechaCreacion)
            .HasColumnName("CreatedAt")
            .IsRequired();

        builder.Property(habito => habito.FechaInicio)
            .HasColumnName("StartDate")
            .IsRequired();

        builder.Property(habito => habito.Estado)
            .IsRequired();

        builder.HasOne(habito => habito.Usuario)
            .WithMany()
            .HasForeignKey("UsuarioId")
            .IsRequired();

        builder.OwnsMany(habito => habito.DiasProgramados, dias =>
        {
            dias.ToTable("HabitoDiaProgramado");
            dias.WithOwner().HasForeignKey("HabitoId");
            dias.Property(dia => dia.Dia)
                .HasConversion<int>()
                .IsRequired();
            dias.HasKey("HabitoId", nameof(DiaProgramado.Dia));
        });

        builder.Navigation(habito => habito.DiasProgramados)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
