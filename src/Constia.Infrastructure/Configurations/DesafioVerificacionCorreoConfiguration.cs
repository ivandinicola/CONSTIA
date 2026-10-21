using Constia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Constia.Infrastructure.Configurations;

public sealed class DesafioVerificacionCorreoConfiguration : IEntityTypeConfiguration<DesafioVerificacionCorreo>
{
    public void Configure(EntityTypeBuilder<DesafioVerificacionCorreo> b)
    {
        b.ToTable("DesafioVerificacionCorreo", t =>
        {
            t.HasCheckConstraint("CK_DesafioCorreo_Proposito", "[Proposito] = 1");
            t.HasCheckConstraint("CK_DesafioCorreo_Hash", "DATALENGTH([TokenHash]) = 32");
            t.HasCheckConstraint("CK_DesafioCorreo_Fechas", "DATEPART(TZOFFSET,[EmitidoEnUtc]) = 0 AND DATEPART(TZOFFSET,[ExpiraEnUtc]) = 0 AND [ExpiraEnUtc] = DATEADD(hour,24,[EmitidoEnUtc])");
            t.HasCheckConstraint("CK_DesafioCorreo_Consumo", "[ConsumidoEnUtc] IS NULL OR ([InvalidadoEnUtc] IS NULL AND DATEPART(TZOFFSET,[ConsumidoEnUtc]) = 0 AND [ConsumidoEnUtc] >= [EmitidoEnUtc] AND [ConsumidoEnUtc] < [ExpiraEnUtc])");
            t.HasCheckConstraint("CK_DesafioCorreo_Invalidacion", "[InvalidadoEnUtc] IS NULL OR (DATEPART(TZOFFSET,[InvalidadoEnUtc]) = 0 AND [InvalidadoEnUtc] >= [EmitidoEnUtc])");
            t.HasCheckConstraint("CK_DesafioCorreo_Intentos", "[IntentosConfirmacion] >= 0 AND (([IntentosConfirmacion] = 0 AND [VentanaIntentosEnUtc] IS NULL) OR ([IntentosConfirmacion] > 0 AND [VentanaIntentosEnUtc] IS NOT NULL AND DATEPART(TZOFFSET,[VentanaIntentosEnUtc]) = 0))");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.EmailDestino).HasMaxLength(450).IsRequired();
        b.Property(x => x.TokenHash).HasMaxLength(32).IsRequired();
        b.Property<byte[]>("Version").IsRowVersion();
        b.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UsuarioId, x.Proposito }).IsUnique()
            .HasFilter("[ConsumidoEnUtc] IS NULL AND [InvalidadoEnUtc] IS NULL");
        b.HasIndex(x => new { x.UsuarioId, x.Proposito, x.EmitidoEnUtc });
        b.HasIndex(x => x.EmitidoEnUtc);
    }
}
