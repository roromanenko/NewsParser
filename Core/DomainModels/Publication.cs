namespace Core.DomainModels;

public class Publication
{
	public Guid Id { get; init; }
	public Article Article { get; init; } = null!;
	public User? Editor { get; init; }
	public PublishTarget PublishTarget { get; set; } = null!;
	public Guid PublishTargetId { get; set; }
	public string GeneratedContent { get; set; } = string.Empty;
	public PublicationStatus Status { get; set; }
	public DateTimeOffset CreatedAt { get; init; }
	public DateTimeOffset? PublishedAt { get; set; }
	public DateTimeOffset? ApprovedAt { get; set; }
	public List<PublishLog> PublishLogs { get; set; } = [];

	public Platform Platform => PublishTarget.Platform;

	public Guid? EventId { get; set; }
	public Event? Event { get; set; }
	public Guid? ParentPublicationId { get; set; }
	public Publication? ParentPublication { get; set; }

	public string? UpdateContext { get; set; }

	public List<Guid> SelectedMediaFileIds { get; set; } = [];
	public Guid? ReviewedByEditorId { get; set; }
	public DateTimeOffset? RejectedAt { get; set; }
	public string? RejectionReason { get; set; }
}

public enum Platform
{
	Telegram,
	Website,
	Instagram
}

public enum PublicationStatus
{
	Created,
	GenerationInProgress,
	ContentReady,
	Approved,
	Rejected,
	Published,
	Failed
}
