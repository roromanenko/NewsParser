namespace Infrastructure.Persistence.Entity;

public class ArticleEntity
{
	public Guid Id { get; init; }
	public List<PublicationEntity> Publications { get; set; } = [];

	// Source fields
	public string? OriginalContent { get; set; }
	public Guid? SourceId { get; set; }
	public SourceEntity? Source { get; set; }
	public string? OriginalUrl { get; set; }
	public DateTimeOffset? PublishedAt { get; set; }
	public string? ExternalId { get; set; }
	public Pgvector.Vector? Embedding { get; set; }

	public string Title { get; set; } = string.Empty;
	public string[] Tags { get; set; } = [];
	public string Category { get; set; } = string.Empty;
	public string Sentiment { get; set; } = string.Empty;
	public DateTimeOffset ProcessedAt { get; set; }
	public string Status { get; set; } = string.Empty;
	public string ModelVersion { get; set; } = string.Empty;
	public string Language { get; set; } = string.Empty;
	public string? Summary { get; set; }
	public List<string> KeyFacts { get; set; } = [];
	public string? RejectionReason { get; set; }
	public int RetryCount { get; set; }
	public Guid? EventId { get; set; }
	public EventEntity? Event { get; set; }
	public string? Role { get; set; }
	public bool WasReclassified { get; set; }
	public DateTimeOffset? AddedToEventAt { get; set; }
	public List<MediaFileEntity> MediaFiles { get; set; } = [];
}