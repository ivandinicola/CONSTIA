using Constia.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Constia.Infrastructure.Configurations;

public sealed class CategoriaHabitoConfiguration : IEntityTypeConfiguration<CategoriaHabito>
{
    public void Configure(EntityTypeBuilder<CategoriaHabito> builder)
    {
        builder.ToTable("CategoriaHabito");
        builder.HasKey(categoria => categoria.Id);
        builder.Property(categoria => categoria.Id).ValueGeneratedNever();
        builder.Property(categoria => categoria.Nombre).IsRequired();
        builder.Property(categoria => categoria.Orden).IsRequired();
        builder.HasData(CatalogoCategoriasHabito.Todas);
    }
}
