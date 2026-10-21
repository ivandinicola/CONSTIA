using Constia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Constia.Infrastructure.Configurations;

public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.HasKey(usuario => usuario.Id);
        builder.ToTable("Usuario", t =>
        {
            t.HasTrigger("TR_Usuario_ProtegeVerificacionCorreo");
            t.UseSqlOutputClause(false);
            t.HasCheckConstraint("CK_Usuario_VerificacionCorreo", "([EstadoVerificacionCorreo] IN (0,1) AND [EmailVerificadoEnUtc] IS NULL) OR ([EstadoVerificacionCorreo] = 2 AND [EmailVerificadoEnUtc] IS NOT NULL AND DATEPART(TZOFFSET,[EmailVerificadoEnUtc]) = 0)");
        });
        builder.Property(x => x.EstadoVerificacionCorreo).HasConversion<byte>().HasDefaultValue(EstadoVerificacionCorreo.Pendiente).HasSentinel((EstadoVerificacionCorreo)255);
        builder.Property(x => x.EmailVerificadoEnUtc);
        builder.Property<byte[]>("VersionCorreo").IsRowVersion();

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
