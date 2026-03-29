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
			.HasOne(a => a.RawArticle)
			.WithOne(r => r.Article)
			.HasForeignKey<ArticleEntity>(a => a.RawArticleId);

		builder.HasIndex(a => a.Status);
		builder.HasIndex(a => a.ProcessedAt);

		builder
			.Property(a => a.Status)
			.HasConversion<string>();

		builder
			.Property(a => a.Sentiment)
			.HasConversion<string>();

		builder.ToTable("articles");
	}
}
