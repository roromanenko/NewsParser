namespace Core.DomainModels;

public class EventUpdate
{
	public Guid Id { get; init; }
	public Guid EventId { get; init; }
	public Event Event { get; init; } = null!;

	public Guid ArticleId { get; init; }
	public Article Article { get; init; } = null!;

	public string FactSummary { get; set; } = string.Empty;
	public bool IsPublished { get; set; }
	public DateTimeOffset CreatedAt { get; init; }
}