using Infrastructure.Persistence.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class EventUpdateConfiguration : IEntityTypeConfiguration<EventUpdateEntity>
{
	public void Configure(EntityTypeBuilder<EventUpdateEntity> builder)
	{
		builder.HasKey(eu => eu.Id);

		builder.HasIndex(eu => eu.EventId);
		builder.HasIndex(eu => eu.IsPublished);

		builder
			.HasOne(eu => eu.Event)
			.WithMany(e => e.EventUpdates)
			.HasForeignKey(eu => eu.EventId);

		builder
			.HasOne(eu => eu.Article)
			.WithMany()
			.HasForeignKey(eu => eu.ArticleId);

		builder.ToTable("event_updates");
	}
}