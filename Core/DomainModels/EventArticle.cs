namespace Core.DomainModels;

public class EventArticle
{
	public Guid EventId { get; init; }
	public Event Event { get; init; } = null!;

	public Guid ArticleId { get; init; }
	public Article Article { get; init; } = null!;

	public DateTimeOffset AddedAt { get; init; }
	public EventArticleRole Role { get; set; }
	public bool WasReclassified { get; set; }
}

public enum EventArticleRole
{
	Initiator,
	Update,
	Contradiction
}