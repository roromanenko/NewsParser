namespace Core.DomainModels;

public class Event
{
	public Guid Id { get; init; }
	public string Title { get; set; } = string.Empty;
	public string Summary { get; set; } = string.Empty;
	public EventStatus Status { get; set; }
	public DateTimeOffset FirstSeenAt { get; init; }
	public DateTimeOffset LastUpdatedAt { get; set; }
	public float[]? Embedding { get; set; }

	public int ArticleCount { get; set; } = 0;

	public ImportanceTier? ImportanceTier { get; set; }
	public double? ImportanceBaseScore { get; set; }
	public DateTimeOffset? ImportanceCalculatedAt { get; set; }

	/// <remarks>
	/// Only populated by paged and detail queries (via JOIN COUNT DISTINCT). Will be <c>0</c> in
	/// all other contexts (e.g. GetByIdAsync, GetActiveEventsAsync).
	/// </remarks>
	public int DistinctSourceCount { get; set; }

	public List<Article> Articles { get; set; } = [];
	public List<EventUpdate> EventUpdates { get; set; } = [];
	public List<Contradiction> Contradictions { get; set; } = [];
}

public enum EventStatus
{
	Active,
	Archived
}

public enum ImportanceTier
{
	Breaking,
	High,
	Normal,
	Low
}