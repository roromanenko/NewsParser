namespace Infrastructure.Persistence.Entity;

public class EventUpdateEntity
{
	public Guid Id { get; init; }
	public Guid EventId { get; init; }
	public EventEntity Event { get; init; } = null!;

	public Guid ArticleId { get; init; }
	public ArticleEntity Article { get; init; } = null!;

	public string FactSummary { get; set; } = string.Empty;
	public bool IsPublished { get; set; }
	public DateTimeOffset CreatedAt { get; init; }
}
