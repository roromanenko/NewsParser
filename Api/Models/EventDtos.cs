namespace Api.Models;

public record EventListItemDto(
	Guid Id,
	string Title,
	string Summary,
	string Status,
	DateTimeOffset FirstSeenAt,
	DateTimeOffset LastUpdatedAt,
	int ArticleCount,
	int UnresolvedContradictions,
	string? ImportanceTier,
	double? ImportanceBaseScore,
	int DistinctSourceCount
);

public record EventDetailDto(
	Guid Id,
	string Title,
	string Summary,
	string Status,
	DateTimeOffset FirstSeenAt,
	DateTimeOffset LastUpdatedAt,
	List<EventArticleDto> Articles,
	List<EventUpdateDto> Updates,
	List<ContradictionDto> Contradictions,
	int ReclassifiedCount,
	string? ImportanceTier,
	double? ImportanceBaseScore,
	int DistinctSourceCount
);

public record EventArticleDto(
	Guid ArticleId,
	string Title,
	string? Summary,
	List<string> KeyFacts,
	string Role,
	DateTimeOffset AddedAt,
	List<MediaFileDto> Media
);

public record EventUpdateDto(
	Guid Id,
	string FactSummary,
	bool IsPublished,
	DateTimeOffset CreatedAt
);

public record ContradictionDto(
	Guid Id,
	string Description,
	bool IsResolved,
	DateTimeOffset CreatedAt,
	List<Guid> ArticleIds
);

public record MergeEventsRequest(Guid SourceEventId, Guid TargetEventId);

public record ResolveContradictionRequest(Guid ContradictionId);

public record ReclassifyArticleRequest(Guid ArticleId, Guid TargetEventId, string Role);