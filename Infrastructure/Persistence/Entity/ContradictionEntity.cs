namespace Infrastructure.Persistence.Entity;

public class ContradictionEntity
{
	public Guid Id { get; init; }
	public Guid EventId { get; init; }
	public EventEntity Event { get; init; } = null!;

	public string Description { get; set; } = string.Empty;
	public bool IsResolved { get; set; }
	public DateTimeOffset CreatedAt { get; init; }

	public List<ContradictionArticleEntity> ContradictionArticles { get; set; } = [];
}

public class ContradictionArticleEntity
{
	public Guid ContradictionId { get; init; }
	public ContradictionEntity Contradiction { get; init; } = null!;

	public Guid ArticleId { get; init; }
	public ArticleEntity Article { get; init; } = null!;
}