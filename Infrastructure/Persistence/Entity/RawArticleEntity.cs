using Pgvector;

namespace Infrastructure.Persistence.Entity;

public class RawArticleEntity
{
	public Guid Id { get; init; }
	public string Title { get; set; } = string.Empty;
	public string Content { get; set; } = string.Empty;
	public List<string> Category { get; set; } = [];
	public DateTimeOffset PublishedAt { get; set; }
	public ArticleEntity? Article { get; set; }
	public string Status { get; set; } = string.Empty;
	public Guid SourceId { get; set; }
	public SourceEntity Source { get; set; } = null!;
	public string OriginalUrl { get; set; } = string.Empty;
	public string Language { get; set; } = string.Empty;
	public string? ExternalId { get; set; }
	public int RetryCount { get; set; }

	public Vector? Embedding { get; set; }
}