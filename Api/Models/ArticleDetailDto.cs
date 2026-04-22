namespace Api.Models;

public record MediaFileDto(
	Guid Id,
	Guid? ArticleId,
	string Url,
	string Kind,
	string ContentType,
	long SizeBytes,
	string OwnerKind
);

public record ArticleDetailDto(
	Guid Id,
	string Title,
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
	ArticleEventDto? Event,
	List<MediaFileDto> Media
);

public record ArticleEventDto(
	Guid EventId,
	string EventTitle,
	string EventStatus,
	string Role
);
