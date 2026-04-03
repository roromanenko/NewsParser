using Pgvector;

namespace Infrastructure.Persistence.Entity;

public class EventEntity
{
	public Guid Id { get; init; }
	public string Title { get; set; } = string.Empty;
	public string Summary { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
	public DateTimeOffset FirstSeenAt { get; init; }
	public DateTimeOffset LastUpdatedAt { get; set; }
	public Vector? Embedding { get; set; }

	public int ArticleCount { get; set; } = 0;

	public List<ArticleEntity> Articles { get; set; } = [];
	public List<EventUpdateEntity> EventUpdates { get; set; } = [];
	public List<ContradictionEntity> Contradictions { get; set; } = [];
}