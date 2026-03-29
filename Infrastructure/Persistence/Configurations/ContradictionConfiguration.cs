using Infrastructure.Persistence.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class ContradictionConfiguration : IEntityTypeConfiguration<ContradictionEntity>
{
	public void Configure(EntityTypeBuilder<ContradictionEntity> builder)
	{
		builder.HasKey(c => c.Id);

		builder.HasIndex(c => c.EventId);
		builder.HasIndex(c => c.IsResolved);

		builder
			.HasOne(c => c.Event)
			.WithMany(e => e.Contradictions)
			.HasForeignKey(c => c.EventId);

		builder
			.HasMany(c => c.ContradictionArticles)
			.WithOne(ca => ca.Contradiction)
			.HasForeignKey(ca => ca.ContradictionId);

		builder.ToTable("contradictions");
	}
}

public class ContradictionArticleConfiguration : IEntityTypeConfiguration<ContradictionArticleEntity>
{
	public void Configure(EntityTypeBuilder<ContradictionArticleEntity> builder)
	{
		builder.HasKey(ca => new { ca.ContradictionId, ca.ArticleId });

		builder
			.HasOne(ca => ca.Contradiction)
			.WithMany(c => c.ContradictionArticles)
			.HasForeignKey(ca => ca.ContradictionId);

		builder
			.HasOne(ca => ca.Article)
			.WithMany()
			.HasForeignKey(ca => ca.ArticleId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.ToTable("contradiction_articles");
	}
}