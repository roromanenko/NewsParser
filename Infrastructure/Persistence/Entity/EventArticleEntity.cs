namespace Infrastructure.Persistence.Entity;

public class EventArticleEntity
{
	public Guid EventId { get; init; }
	public EventEntity Event { get; init; } = null!;

	public Guid ArticleId { get; init; }
	public ArticleEntity Article { get; init; } = null!;

	public DateTimeOffset AddedAt { get; init; }
	public string Role { get; set; } = string.Empty;

	public bool WasReclassified { get; set; }
}