namespace Core.DomainModels;

public class Contradiction
{
	public Guid Id { get; init; }
	public Guid EventId { get; init; }
	public Event Event { get; init; } = null!;

	public string Description { get; set; } = string.Empty;
	public bool IsResolved { get; set; }
	public DateTimeOffset CreatedAt { get; init; }

	public List<ContradictionArticle> ContradictionArticles { get; set; } = [];
}

public class ContradictionArticle
{
	public Guid ContradictionId { get; init; }
	public Contradiction Contradiction { get; init; } = null!;

	public Guid ArticleId { get; init; }
	public Article Article { get; init; } = null!;
}