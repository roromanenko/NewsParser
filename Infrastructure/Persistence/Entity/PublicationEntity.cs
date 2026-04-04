namespace Infrastructure.Persistence.Entity;

public class PublicationEntity
{
	public Guid Id { get; init; }
	public Guid ArticleId { get; init; }
	public ArticleEntity Article { get; init; } = null!;
	public Guid? EditorId { get; init; }
	public UserEntity? Editor { get; init; }
	public Guid PublishTargetId { get; set; }
	public PublishTargetEntity PublishTarget { get; set; } = null!;
	public string GeneratedContent { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
	public DateTimeOffset CreatedAt { get; init; }
	public DateTimeOffset? PublishedAt { get; set; }
	public DateTimeOffset? ApprovedAt { get; set; }
	public List<PublishLogEntity> PublishLogs { get; set; } = [];
	public Guid? EventId { get; set; }
	public EventEntity? Event { get; set; }
	public Guid? ParentPublicationId { get; set; }
	public PublicationEntity? ParentPublication { get; set; }
	public string? UpdateContext { get; set; }
}