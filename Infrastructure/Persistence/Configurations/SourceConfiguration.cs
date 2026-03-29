using Infrastructure.Persistence.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class SourceConfiguration : IEntityTypeConfiguration<SourceEntity>
{
	public void Configure(EntityTypeBuilder<SourceEntity> builder)
	{
		builder.HasKey(s => s.Id);
		builder.HasIndex(s => s.IsActive);
		builder.HasIndex(s => s.Url).IsUnique();

		builder
			.HasMany(s => s.RawArticles)
			.WithOne(r => r.Source)
			.HasForeignKey(r => r.SourceId);

		builder
			.Property(s => s.Type)
			.HasConversion<string>();

		builder.ToTable("sources");
	}
}
