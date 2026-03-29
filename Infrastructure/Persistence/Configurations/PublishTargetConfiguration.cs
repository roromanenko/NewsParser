using Infrastructure.Persistence.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class PublishTargetConfiguration : IEntityTypeConfiguration<PublishTargetEntity>
{
	public void Configure(EntityTypeBuilder<PublishTargetEntity> builder)
	{
		builder.HasKey(t => t.Id);
		builder.HasIndex(t => t.IsActive);
		builder.HasIndex(t => t.Platform);
		builder
			.HasMany(t => t.Publications)
			.WithOne(p => p.PublishTarget)
			.HasForeignKey(p => p.PublishTargetId);
		builder
			.Property(t => t.Platform)
			.HasConversion<string>();
		builder.ToTable("publish_targets");
	}
}