namespace Api.Models;

public record SourceDto(
	Guid Id,
	string Name,
	string Url,
	string Type,
	bool IsActive,
	DateTimeOffset? LastFetchedAt
);

public record CreateSourceRequest(string Name, string Url, string Type);

public record UpdateSourceRequest(string Name, string Url, bool IsActive);