namespace Core.DomainModels;

public class Article
{
	public Guid Id { get; init; }
	public List<Publication> Publications { get; set; } = [];

	public string? OriginalContent { get; set; }
	public Guid? SourceId { get; set; }
	public string? OriginalUrl { get; set; }
	public DateTimeOffset? PublishedAt { get; set; }
	public string? ExternalId { get; set; }
	public float[]? Embedding { get; set; }

	//Ai
	public string Title { get; set; } = string.Empty;
	public List<string> Tags { get; set; } = [];
	public string Category { get; set; } = string.Empty;
	public Sentiment Sentiment { get; set; }
	public string? Summary { get; set; }
	public List<string> KeyFacts { get; set; } = [];

	//Service
	public DateTimeOffset ProcessedAt { get; set; }
	public ArticleStatus Status { get; set; }
	public string ModelVersion { get; set; } = string.Empty;

	public string Language { get; set; } = string.Empty;

	public string? RejectionReason { get; set; }

	public int RetryCount { get; set; }

	public Guid? EventId { get; set; }
	public ArticleRole? Role { get; set; }
	public bool WasReclassified { get; set; }
	public DateTimeOffset? AddedToEventAt { get; set; }
}

public enum Sentiment
{
	Positive,
	Negative,
	Neutral
}

public enum ArticleStatus
{
	Pending,
	Analyzing,
	AnalysisDone,
	Rejected
}
