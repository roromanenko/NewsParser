using Infrastructure.Persistence.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class EventArticleConfiguration : IEntityTypeConfiguration<EventArticleEntity>
{
	public void Configure(EntityTypeBuilder<EventArticleEntity> builder)
	{
		builder.HasKey(ea => new { ea.EventId, ea.ArticleId });

		builder
			.HasOne(ea => ea.Event)
			.WithMany(e => e.EventArticles)
			.HasForeignKey(ea => ea.EventId);

		builder
			.HasOne(ea => ea.Article)
			.WithMany(a => a.EventArticles)
			.HasForeignKey(ea => ea.ArticleId);

		builder
			.Property(ea => ea.Role)
			.HasConversion<string>();

		builder.ToTable("event_articles");
	}
}