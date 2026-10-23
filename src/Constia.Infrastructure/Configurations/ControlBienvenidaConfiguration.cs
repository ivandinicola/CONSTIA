using Constia.Infrastructure.Bienvenida;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Constia.Infrastructure.Configurations;
public sealed class ControlBienvenidaConfiguration : IEntityTypeConfiguration<ControlBienvenida>
{
    internal static readonly (string Name,string Sql)[] Checks =
    [
        ("CK_ControlBienvenida_Identidad","[Id]=1 AND [VersionEsquema]=1 AND [ProtocoloEscritor]=1"),
        ("CK_ControlBienvenida_Epoca","([EpocaCaptura] IS NULL AND [CapturaHabilitada]=0 AND [InicioCapturaEnUtc] IS NULL AND [FinCapturaEnUtc] IS NULL) OR ([EpocaCaptura] IS NOT NULL AND [EpocaCaptura]<>'00000000-0000-0000-0000-000000000000' AND [InicioCapturaEnUtc] IS NOT NULL AND DATEPART(TZOFFSET,[InicioCapturaEnUtc])=0 AND (([CapturaHabilitada]=1 AND [FinCapturaEnUtc] IS NULL) OR ([CapturaHabilitada]=0 AND [FinCapturaEnUtc] IS NOT NULL AND DATEPART(TZOFFSET,[FinCapturaEnUtc])=0 AND [FinCapturaEnUtc]>=[InicioCapturaEnUtc])))"),
        ("CK_ControlBienvenida_Pausa","([PausaProveedor]=0 AND [MotivoPausa] IS NULL) OR ([PausaProveedor]=1 AND [MotivoPausa] IS NOT NULL AND [MotivoPausa]>=0)")
    ];
    public void Configure(EntityTypeBuilder<ControlBienvenida> b)
    {
        b.ToTable("ControlBienvenida",t=>{foreach(var c in Checks)t.HasCheckConstraint(c.Name,c.Sql);});
        b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
        b.Property<byte[]>("Version").IsRowVersion().IsRequired();
        b.HasData(new {Id=1,VersionEsquema=1,ProtocoloEscritor=1,CapturaHabilitada=false,EnvioHabilitado=false,PausaProveedor=false});
    }
}
