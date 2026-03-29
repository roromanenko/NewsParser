namespace Api.Models;

public record ArticleListItemDto(
	Guid Id,
	string Title,
	string Category,
	List<string> Tags,
	string Sentiment,
	string Language,
	string? Summary,
	DateTimeOffset ProcessedAt
);