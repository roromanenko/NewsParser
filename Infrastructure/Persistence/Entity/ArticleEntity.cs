namespace Infrastructure.Persistence.Entity;

public class ArticleEntity
{
	public Guid Id { get; init; }
	public Guid RawArticleId { get; init; }
	public RawArticleEntity RawArticle { get; init; } = null!;
	public List<PublicationEntity> Publications { get; set; } = [];
	public string Title { get; set; } = string.Empty;
	public string Content { get; set; } = string.Empty;
	public List<string> Tags { get; set; } = [];
	public string Category { get; set; } = string.Empty;
	public string Sentiment { get; set; } = string.Empty;
	public DateTimeOffset ProcessedAt { get; set; }
	public string Status { get; set; } = string.Empty;
	public string ModelVersion { get; set; } = string.Empty;
	public string Language { get; set; } = string.Empty;
	public string? Summary { get; set; }
	public Guid? RejectedByEditorId { get; set; }
	public string? RejectionReason { get; set; }
	public int RetryCount { get; set; }
	public List<EventArticleEntity> EventArticles { get; set; } = [];
}