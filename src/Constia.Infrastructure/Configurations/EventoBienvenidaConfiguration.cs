using Constia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Constia.Infrastructure.Configurations;
public sealed class EventoBienvenidaConfiguration : IEntityTypeConfiguration<EventoBienvenida>
{
    internal static readonly (string Name,string Sql)[] Checks =
    [
        ("CK_Bienvenida_Identidad","[Id] <> '00000000-0000-0000-0000-000000000000' AND [EpocaCaptura] <> '00000000-0000-0000-0000-000000000000' AND [Tipo] = 1"),
        ("CK_Bienvenida_Fechas","DATEPART(TZOFFSET,[ConfirmadoEnUtc])=0 AND DATEPART(TZOFFSET,[CapturadoEnUtc])=0 AND DATEPART(TZOFFSET,[ExpiraEnUtc])=0 AND [ExpiraEnUtc]=DATEADD(hour,24,[CapturadoEnUtc])"),
        ("CK_Bienvenida_Estado","[Estado] BETWEEN 1 AND 8 AND [MotivoFinal] BETWEEN 0 AND 7 AND [Reservas] BETWEEN 0 AND 3 AND [LeaseGeneracion]>=0"),
        ("CK_Bienvenida_Final","([Estado] BETWEEN 1 AND 5 AND [FinalizadoEnUtc] IS NULL AND [DatosDepuradosEnUtc] IS NULL AND [Destinatario] IS NOT NULL AND [MotivoFinal]=0) OR ([Estado] BETWEEN 6 AND 8 AND [FinalizadoEnUtc] IS NOT NULL AND DATEPART(TZOFFSET,[FinalizadoEnUtc])=0 AND [FinalizadoEnUtc]>=[CapturadoEnUtc] AND [ProximaEjecucionEnUtc] IS NULL AND [LeaseId] IS NULL AND ([Estado]<>7 OR ([HuboIncertidumbre]=0 AND [MotivoFinal]<>0)) AND ([Estado]<>8 OR ([HuboIncertidumbre]=1 AND [MotivoFinal]<>0)) AND ([Estado]<>6 OR ([Reservas]>0 AND [MotivoFinal]=0)))"),
        ("CK_Bienvenida_Lease","([Estado] IN (2,3) AND [LeaseId] IS NOT NULL AND [LeaseId]<>'00000000-0000-0000-0000-000000000000' AND [LeaseHastaUtc] IS NOT NULL AND [LeaseGeneracion]>0 AND [UltimoLeaseHastaUtc] IS NOT NULL AND [UltimoLeaseHastaUtc]>=[LeaseHastaUtc]) OR ([Estado] NOT IN (2,3) AND [LeaseId] IS NULL AND [LeaseHastaUtc] IS NULL)"),
        ("CK_Bienvenida_Reserva","([Reservas]=0 AND [PrimeraReservaEnUtc] IS NULL AND [HorizonteHttpEnUtc] IS NULL) OR ([Reservas]>0 AND [PrimeraReservaEnUtc] IS NOT NULL AND [HorizonteHttpEnUtc] IS NOT NULL AND [PrimeraReservaEnUtc]>=[CapturadoEnUtc] AND [HorizonteHttpEnUtc]=CASE WHEN DATEADD(hour,23,[PrimeraReservaEnUtc])<[ExpiraEnUtc] THEN DATEADD(hour,23,[PrimeraReservaEnUtc]) ELSE [ExpiraEnUtc] END)"),
        ("CK_Bienvenida_Payload","([PayloadUtf8] IS NULL AND [PayloadSha256] IS NULL) OR ([PayloadUtf8] IS NOT NULL AND DATALENGTH([PayloadUtf8]) BETWEEN 1 AND 16384 AND [PayloadSha256] IS NOT NULL AND DATALENGTH([PayloadSha256])=32 AND [PerfilProveedorVersion] IS NOT NULL)"),
        ("CK_Bienvenida_Coherencia","([Estado]<>1 OR ([Reservas]=0 AND [HuboIncertidumbre]=0)) AND ([Estado] NOT IN (3,4) OR [Reservas]>0) AND ([Reservas]=0 OR [PayloadUtf8] IS NOT NULL OR [DatosDepuradosEnUtc] IS NOT NULL)"),
        ("CK_Bienvenida_Depuracion","[DatosDepuradosEnUtc] IS NULL OR ([Estado] BETWEEN 6 AND 8 AND [Destinatario] IS NULL AND [PayloadUtf8] IS NULL AND [PayloadSha256] IS NULL AND DATEPART(TZOFFSET,[DatosDepuradosEnUtc])=0 AND [DatosDepuradosEnUtc]>=[FinalizadoEnUtc])"),
        ("CK_Bienvenida_FechasOpcionales","([ProximaEjecucionEnUtc] IS NULL OR DATEPART(TZOFFSET,[ProximaEjecucionEnUtc])=0) AND ([PrimeraReservaEnUtc] IS NULL OR DATEPART(TZOFFSET,[PrimeraReservaEnUtc])=0) AND ([HorizonteHttpEnUtc] IS NULL OR DATEPART(TZOFFSET,[HorizonteHttpEnUtc])=0) AND ([LeaseHastaUtc] IS NULL OR DATEPART(TZOFFSET,[LeaseHastaUtc])=0) AND ([UltimoLeaseHastaUtc] IS NULL OR DATEPART(TZOFFSET,[UltimoLeaseHastaUtc])=0)")
    ];
    public void Configure(EntityTypeBuilder<EventoBienvenida> b)
    {
        b.ToTable("EventoBienvenida",t=>{foreach(var c in Checks)t.HasCheckConstraint(c.Name,c.Sql);});
        b.HasKey(x=>x.Id); b.Property(x=>x.Id).ValueGeneratedNever();
        b.Ignore(x=>x.ClaveIdempotencia);
        b.Property(x=>x.Estado).HasConversion<byte>(); b.Property(x=>x.MotivoFinal).HasConversion<byte>();
        b.Property(x=>x.Destinatario).HasColumnType("nvarchar(max)");
        b.Property(x=>x.PlantillaVersion).HasMaxLength(32).IsUnicode(false).IsRequired();
        b.Property(x=>x.PerfilProveedorVersion).HasMaxLength(64).IsUnicode(false);
        b.Property(x=>x.IdProveedor).HasMaxLength(256).IsUnicode(false);
        b.Property(x=>x.PayloadSha256).HasColumnType("binary(32)");
        b.Property<byte[]>("Version").IsRowVersion().IsRequired();
        b.HasOne<Usuario>().WithMany().HasForeignKey(x=>x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>x.UsuarioId).IsUnique().HasDatabaseName("UX_EventoBienvenida_UsuarioId");
        b.HasIndex(x=>new{x.Estado,x.ProximaEjecucionEnUtc,x.Id});
    }
}
