using Infrastructure.Persistence.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class PublicationConfiguration : IEntityTypeConfiguration<PublicationEntity>
{
	public void Configure(EntityTypeBuilder<PublicationEntity> builder)
	{
		builder.HasKey(p => p.Id);
		builder
			.HasOne(p => p.Editor)
			.WithMany(u => u.Publications);
		builder
			.HasOne(p => p.PublishTarget)
			.WithMany(t => t.Publications)
			.HasForeignKey(p => p.PublishTargetId);
		builder.HasIndex(p => p.Status);
		builder.HasIndex(p => p.PublishTargetId);
		builder
			.HasMany(p => p.PublishLogs)
			.WithOne(l => l.Publication)
			.HasForeignKey(l => l.PublicationId);
		builder
			.Property(p => p.Status)
			.HasConversion<string>();
		builder.ToTable("publications");
		builder
			.HasOne(p => p.ParentPublication)
			.WithMany()
			.HasForeignKey(p => p.ParentPublicationId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasIndex(p => p.EventId);
		builder.HasIndex(p => p.ParentPublicationId);
	}
}