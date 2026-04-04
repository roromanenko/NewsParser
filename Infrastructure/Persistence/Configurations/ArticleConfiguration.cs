using Infrastructure.Persistence.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class ArticleConfiguration : IEntityTypeConfiguration<ArticleEntity>
{
	public void Configure(EntityTypeBuilder<ArticleEntity> builder)
	{
		builder.HasKey(a => a.Id);

		builder
			.HasMany(a => a.Publications)
			.WithOne(p => p.Article);

		builder
			.HasOne(a => a.Source)
			.WithMany(s => s.Articles)
			.HasForeignKey(a => a.SourceId)
			.IsRequired(false);

		builder
			.HasOne(a => a.Event)
			.WithMany(e => e.Articles)
			.HasForeignKey(a => a.EventId)
			.IsRequired(false)
			.OnDelete(DeleteBehavior.SetNull);

		builder.HasIndex(a => a.Status);
		builder.HasIndex(a => a.ProcessedAt);
		builder.HasIndex(a => a.EventId);
		builder.HasIndex(a => new { a.SourceId, a.ExternalId }).IsUnique().HasFilter("source_id IS NOT NULL AND external_id IS NOT NULL");

		builder
			.Property(a => a.Status)
			.HasConversion<string>();

		builder
			.Property(a => a.Sentiment)
			.HasConversion<string>();

		builder
			.Property(a => a.Role)
			.HasConversion<string>();

		builder
			.Property(a => a.KeyFacts)
			.HasColumnType("jsonb");

		builder
			.Property(a => a.Embedding)
			.HasColumnType("vector(768)");

		builder
			.HasIndex(a => a.Embedding)
			.HasMethod("hnsw")
			.HasOperators("vector_cosine_ops");

		builder.ToTable("articles");
	}
}
