namespace Infrastructure.Persistence.Entity;

public class EventEntity
{
	public Guid Id { get; init; }
	public string Title { get; set; } = string.Empty;
	public string Summary { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
	public DateTimeOffset FirstSeenAt { get; init; }
	public DateTimeOffset LastUpdatedAt { get; set; }
	public Pgvector.Vector? Embedding { get; set; }
	public Guid ProjectId { get; set; }

	public int ArticleCount { get; set; } = 0;

	public string? ImportanceTier { get; set; }
	public double? ImportanceBaseScore { get; set; }
	public DateTimeOffset? ImportanceCalculatedAt { get; set; }

	/// <summary>Populated by paged/detail queries via COUNT(DISTINCT), not a stored column.</summary>
	public int DistinctSourceCount { get; set; }

	public List<ArticleEntity> Articles { get; set; } = [];
	public List<EventUpdateEntity> EventUpdates { get; set; } = [];
	public List<ContradictionEntity> Contradictions { get; set; } = [];
}
