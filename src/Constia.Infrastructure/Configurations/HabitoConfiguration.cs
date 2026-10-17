using Constia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Constia.Infrastructure.Configurations;

public class HabitoConfiguration : IEntityTypeConfiguration<Habito>
{
    public void Configure(EntityTypeBuilder<Habito> builder)
    {
        builder.ToTable("Habito");
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

        builder.Property<long>("ConcurrencyVersion")
            .HasColumnName("ConcurrencyVersion")
            .IsConcurrencyToken()
            .ValueGeneratedNever()
            .IsRequired();

        builder.HasOne(habito => habito.Usuario)
            .WithMany()
            .HasForeignKey("UsuarioId")
            .IsRequired();

        builder.HasMany(habito => habito.Programaciones)
            .WithOne()
            .HasForeignKey("HabitoId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(habito => habito.Programaciones)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(habito => habito.PeriodosEstado)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(habito => habito.DiasProgramados);
    }
}
