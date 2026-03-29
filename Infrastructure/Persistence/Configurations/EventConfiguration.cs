using Infrastructure.Persistence.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class EventConfiguration : IEntityTypeConfiguration<EventEntity>
{
	public void Configure(EntityTypeBuilder<EventEntity> builder)
	{
		builder.HasKey(e => e.Id);

		builder.HasIndex(e => e.Status);
		builder.HasIndex(e => e.LastUpdatedAt);
		builder.HasIndex(e => e.FirstSeenAt);

		builder
			.Property(e => e.Status)
			.HasConversion<string>();

		builder
			.HasMany(e => e.EventArticles)
			.WithOne(ea => ea.Event)
			.HasForeignKey(ea => ea.EventId);

		builder
			.HasMany(e => e.EventUpdates)
			.WithOne(eu => eu.Event)
			.HasForeignKey(eu => eu.EventId);

		builder
			.HasMany(e => e.Contradictions)
			.WithOne(c => c.Event)
			.HasForeignKey(c => c.EventId);

		builder
			.Property(e => e.Embedding)
			.HasColumnType("vector(768)");

		builder.ToTable("events");
	}
}