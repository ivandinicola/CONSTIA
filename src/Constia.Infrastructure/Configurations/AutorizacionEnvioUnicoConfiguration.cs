using Constia.Infrastructure.Correo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Constia.Infrastructure.Configurations;

public sealed class AutorizacionEnvioUnicoConfiguration : IEntityTypeConfiguration<AutorizacionEnvioUnico>
{
    public void Configure(EntityTypeBuilder<AutorizacionEnvioUnico> b)
    {
        b.ToTable("AutorizacionEnvioUnico", t =>
        {
            t.HasCheckConstraint("CK_AutorizacionEnvio_Identidad", "DATALENGTH([IdentidadDestinatario]) = 32");
            t.HasCheckConstraint("CK_AutorizacionEnvio_Ids", "[EnsayoId] <> '00000000-0000-0000-0000-000000000000' AND [OperacionId] <> '00000000-0000-0000-0000-000000000000'");
            t.HasCheckConstraint("CK_AutorizacionEnvio_Estado", "([Estado] = 0 AND [ConsumidaEnUtc] IS NULL AND [ResultadoEnUtc] IS NULL) OR ([Estado] = 1 AND [ConsumidaEnUtc] IS NOT NULL AND [ResultadoEnUtc] IS NULL) OR ([Estado] IN (2,3) AND [ConsumidaEnUtc] IS NOT NULL AND [ResultadoEnUtc] IS NOT NULL)");
            t.HasCheckConstraint("CK_AutorizacionEnvio_Fechas", "DATEPART(TZOFFSET,[CreadaEnUtc]) = 0 AND ([ConsumidaEnUtc] IS NULL OR (DATEPART(TZOFFSET,[ConsumidaEnUtc]) = 0 AND [ConsumidaEnUtc] >= [CreadaEnUtc])) AND ([ResultadoEnUtc] IS NULL OR (DATEPART(TZOFFSET,[ResultadoEnUtc]) = 0 AND [ResultadoEnUtc] >= [ConsumidaEnUtc]))");
        });
        b.HasKey(x => x.EnsayoId);
        b.HasIndex(x => x.OperacionId).IsUnique();
        b.Ignore(x => x.IdentidadDestinatario);
        b.Property<byte[]>("_identidadDestinatario").HasColumnName("IdentidadDestinatario").HasColumnType("binary(32)").IsRequired();
        b.Property(x => x.Estado).HasConversion<int>();
    }
}
