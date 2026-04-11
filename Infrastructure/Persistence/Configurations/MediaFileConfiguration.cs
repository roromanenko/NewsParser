using Infrastructure.Persistence.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class MediaFileConfiguration : IEntityTypeConfiguration<MediaFileEntity>
{
	public void Configure(EntityTypeBuilder<MediaFileEntity> builder)
	{
		builder.HasKey(m => m.Id);

		builder.ToTable("media_files");

		builder.HasIndex(m => m.ArticleId);
		builder.HasIndex(m => new { m.ArticleId, m.OriginalUrl }).IsUnique();

		builder
			.Property(m => m.Kind)
			.HasConversion<string>();
	}
}
