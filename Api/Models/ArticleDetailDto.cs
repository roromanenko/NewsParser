namespace Api.Models;

public record ArticleDetailDto(
	Guid Id,
	string Title,
	string Content,
	string Category,
	List<string> Tags,
	string Sentiment,
	string Language,
	string? Summary,
	DateTimeOffset ProcessedAt,
	string ModelVersion,
	RawArticleDto Source,
	ArticleEventDto? Event
);

public record RawArticleDto(
	Guid Id,
	string Title,
	string OriginalUrl,
	DateTimeOffset PublishedAt,
	string Language
);

public record ArticleEventDto(
	Guid EventId,
	string EventTitle,
	string EventStatus,
	string Role
);