namespace Core.DomainModels;

public class Article
{
	public Guid Id { get; init; }
	public RawArticle RawArticle { get; init; } = null!;
	public List<Publication> Publications { get; set; } = [];

	//Ai
	public string Title { get; set; } = string.Empty;
	public string Content { get; set; } = string.Empty;
	public List<string> Tags { get; set; } = [];
	public string Category { get; set; } = string.Empty;
	public Sentiment Sentiment { get; set; }

	//Service
	public DateTimeOffset ProcessedAt { get; set; }
	public ArticleStatus Status { get; set; }
	public string ModelVersion { get; set; } = string.Empty;

	public string Language { get; set; } = string.Empty;
	public string? Summary { get; set; }

	public Guid? RejectedByEditorId { get; set; }
	public string? RejectionReason { get; set; }

	public int RetryCount { get; set; }

	public List<EventArticle> EventArticles { get; set; } = [];
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
	Classifying,
	Processing,
	Approved,
	Rejected,
	Published
}