using Infrastructure.Persistence.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class PublishLogConfiguration : IEntityTypeConfiguration<PublishLogEntity>
{
	public void Configure(EntityTypeBuilder<PublishLogEntity> builder)
	{
		builder.HasKey(l => l.Id);
		builder.HasIndex(l => l.PublicationId);

		builder
			.HasOne(l => l.Publication)
			.WithMany(p => p.PublishLogs)
			.HasForeignKey(l => l.PublicationId);

		builder
			.Property(l => l.Status)
			.HasConversion<string>();

		builder.ToTable("publish_logs");
	}
}
