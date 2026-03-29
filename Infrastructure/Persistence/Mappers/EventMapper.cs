using Core.DomainModels;
using Infrastructure.Persistence.Entity;
using Pgvector;

namespace Infrastructure.Persistence.Mappers;

public static class EventMapper
{
	public static Event ToDomain(this EventEntity entity) => new()
	{
		Id = entity.Id,
		Title = entity.Title,
		Summary = entity.Summary,
		Status = Enum.Parse<EventStatus>(entity.Status),
		FirstSeenAt = entity.FirstSeenAt,
		LastUpdatedAt = entity.LastUpdatedAt,
		Embedding = entity.Embedding?.ToArray(),
		Articles = entity.Articles?.Select(a => a.ToDomain()).ToList() ?? [],
		EventUpdates = entity.EventUpdates?.Select(eu => eu.ToDomain()).ToList() ?? [],
		Contradictions = entity.Contradictions?.Select(c => c.ToDomain()).ToList() ?? [],
	};

	public static EventEntity ToEntity(this Event domain) => new()
	{
		Id = domain.Id,
		Title = domain.Title,
		Summary = domain.Summary,
		Status = domain.Status.ToString(),
		FirstSeenAt = domain.FirstSeenAt,
		LastUpdatedAt = domain.LastUpdatedAt,
		Embedding = domain.Embedding != null ? new Vector(domain.Embedding) : null,
	};

	public static EventUpdate ToDomain(this EventUpdateEntity entity) => new()
	{
		Id = entity.Id,
		EventId = entity.EventId,
		ArticleId = entity.ArticleId,
		FactSummary = entity.FactSummary,
		IsPublished = entity.IsPublished,
		CreatedAt = entity.CreatedAt,
	};

	public static EventUpdateEntity ToEntity(this EventUpdate domain) => new()
	{
		Id = domain.Id,
		EventId = domain.EventId,
		ArticleId = domain.ArticleId,
		FactSummary = domain.FactSummary,
		IsPublished = domain.IsPublished,
		CreatedAt = domain.CreatedAt,
	};

	public static Contradiction ToDomain(this ContradictionEntity entity) => new()
	{
		Id = entity.Id,
		EventId = entity.EventId,
		Description = entity.Description,
		IsResolved = entity.IsResolved,
		CreatedAt = entity.CreatedAt,
		ContradictionArticles = entity.ContradictionArticles?
		.Select(ca => ca.ToDomain()).ToList() ?? [],
	};

	public static ContradictionEntity ToEntity(this Contradiction domain) => new()
	{
		Id = domain.Id,
		EventId = domain.EventId,
		Description = domain.Description,
		IsResolved = domain.IsResolved,
		CreatedAt = domain.CreatedAt,
	};

	public static ContradictionArticle ToDomain(this ContradictionArticleEntity entity) => new()
	{
		ContradictionId = entity.ContradictionId,
		ArticleId = entity.ArticleId,
	};

	public static ContradictionArticleEntity ToEntity(this ContradictionArticle domain) => new()
	{
		ContradictionId = domain.ContradictionId,
		ArticleId = domain.ArticleId,
	};
}