using Constia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Constia.Infrastructure.Configurations;

public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.HasKey(usuario => usuario.Id);

        builder.Property(usuario => usuario.Nombre)
            .IsRequired();

        builder.Property(usuario => usuario.Email)
            .IsRequired();

        builder.HasIndex(usuario => usuario.Email)
            .IsUnique();

        builder.Property(usuario => usuario.PasswordHash)
            .IsRequired();

        builder.Property(usuario => usuario.TimeZoneId)
            .HasMaxLength(100)
            .IsRequired();
    }
}
