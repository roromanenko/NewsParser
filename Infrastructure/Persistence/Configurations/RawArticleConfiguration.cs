using Infrastructure.Persistence.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class RawArticleConfiguration : IEntityTypeConfiguration<RawArticleEntity>
{
	public void Configure(EntityTypeBuilder<RawArticleEntity> builder)
	{
		builder.HasKey(r => r.Id);

		builder.HasIndex(r => new { r.SourceId, r.ExternalId }).IsUnique();

		builder.HasIndex(r => r.Status);

		builder
			.HasOne(r => r.Source)
			.WithMany(s => s.RawArticles)
			.HasForeignKey(r => r.SourceId);

		builder.Property(r => r.Status)
			.HasConversion<string>();

		builder.Property(r => r.Language)
			.HasMaxLength(10);

		builder.ToTable("raw_articles");

		builder.Property(r => r.Embedding)
			.HasColumnType("vector(768)");

		builder.HasIndex(r => r.Embedding)
			.HasMethod("hnsw")
			.HasOperators("vector_cosine_ops");
	}
}