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
	List<string> KeyFacts,
	DateTimeOffset ProcessedAt,
	string ModelVersion,
	string? OriginalUrl,
	DateTimeOffset? PublishedAt,
	ArticleEventDto? Event
);

public record ArticleEventDto(
	Guid EventId,
	string EventTitle,
	string EventStatus,
	string Role
);
