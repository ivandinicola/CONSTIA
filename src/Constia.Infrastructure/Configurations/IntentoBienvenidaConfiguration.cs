using Constia.Domain;
using Constia.Infrastructure.Bienvenida;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Constia.Infrastructure.Configurations;
public sealed class IntentoBienvenidaConfiguration : IEntityTypeConfiguration<IntentoBienvenida>
{
    internal static readonly (string Name,string Sql)[] Checks =
    [
        ("CK_IntentoBienvenida_Identidad","[Id]<>'00000000-0000-0000-0000-000000000000' AND [Ordinal] BETWEEN 1 AND 3 AND [LeaseGeneracion]>0 AND [PropietarioLeaseId]<>'00000000-0000-0000-0000-000000000000'"),
        ("CK_IntentoBienvenida_Fechas","DATEPART(TZOFFSET,[ReservadoEnUtc])=0 AND ([FinalizadoEnUtc] IS NULL OR (DATEPART(TZOFFSET,[FinalizadoEnUtc])=0 AND [FinalizadoEnUtc]>=[ReservadoEnUtc]))"),
        ("CK_IntentoBienvenida_Estado","[Estado] BETWEEN 1 AND 6 AND (([Estado]=1 AND [FinalizadoEnUtc] IS NULL AND [HttpIniciadoConfirmado] IS NULL AND [MotivoEntrega] IS NULL AND [HttpStatus] IS NULL) OR ([Estado]<>1 AND [FinalizadoEnUtc] IS NOT NULL)) AND ([Estado]<>2 OR ([HttpIniciadoConfirmado] IS NOT NULL AND [HttpIniciadoConfirmado]=1)) AND ([Estado]<>5 OR ([HttpIniciadoConfirmado] IS NOT NULL AND [HttpIniciadoConfirmado]=0))"),
        ("CK_IntentoBienvenida_Resultado","([HttpStatus] IS NULL OR [HttpStatus] BETWEEN 100 AND 599) AND ([MotivoEntrega] IS NULL OR [MotivoEntrega]>=0)")
    ];
    public void Configure(EntityTypeBuilder<IntentoBienvenida> b)
    {
        b.ToTable("IntentoBienvenida",t=>{foreach(var c in Checks)t.HasCheckConstraint(c.Name,c.Sql);});
        b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();b.Property(x=>x.Estado).HasConversion<byte>();
        b.Property<byte[]>("Version").IsRowVersion().IsRequired();
        b.HasOne<EventoBienvenida>().WithMany().HasForeignKey(x=>x.EventoId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.EventoId,x.Ordinal}).IsUnique().HasDatabaseName("UX_IntentoBienvenida_EventoId_Ordinal");
        b.HasIndex(x=>new{x.ReservadoEnUtc,x.EventoId});
    }
}
