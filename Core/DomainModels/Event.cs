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

	public List<Article> Articles { get; set; } = [];
	public List<EventUpdate> EventUpdates { get; set; } = [];
	public List<Contradiction> Contradictions { get; set; } = [];
}

public enum EventStatus
{
	Active,
	Approved,
	Rejected,
	Resolved,
	Archived
}