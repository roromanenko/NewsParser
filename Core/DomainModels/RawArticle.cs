namespace Core.DomainModels;

public class RawArticle
{
	public Guid Id { get; init; }
	public string Title { get; set; } = string.Empty;
	public string Content { get; set; } = string.Empty;
	public List<string> Category { get; set; } = [];
	public DateTimeOffset PublishedAt { get; set; }


	public RawArticleStatus Status { get; set; }

	public Guid SourceId { get; set; }
	public Source Source { get; set; } = null!;
	public string OriginalUrl { get; set; } = string.Empty;

	public string Language { get; set; } = string.Empty;
	public string? ExternalId { get; set; }

	public int RetryCount { get; set; }

	public float[]? Embedding { get; set; }
}

public enum RawArticleStatus
{
	Pending,
	Analyzing,
	Rejected,
	Completed
}