using Core.DomainModels;
using Infrastructure.Persistence.Entity;
using Pgvector;

namespace Infrastructure.Persistence.Mappers;

public static class RawArticleMapper
{
	public static RawArticle ToDomain(this RawArticleEntity entity) => new()
	{
		Id = entity.Id,
		Title = entity.Title,
		Content = entity.Content,
		Category = entity.Category,
		PublishedAt = entity.PublishedAt,
		Status = Enum.Parse<RawArticleStatus>(entity.Status),
		SourceId = entity.SourceId,
		OriginalUrl = entity.OriginalUrl,
		Language = entity.Language,
		ExternalId = entity.ExternalId,
		RetryCount = entity.RetryCount,
		Embedding = entity.Embedding != null ? entity.Embedding.ToArray() : null,
	};

	public static RawArticleEntity ToEntity(this RawArticle domain) => new()
	{
		Id = domain.Id,
		Title = domain.Title,
		Content = domain.Content,
		Category = domain.Category,
		PublishedAt = domain.PublishedAt,
		Status = domain.Status.ToString(),
		SourceId = domain.SourceId,
		OriginalUrl = domain.OriginalUrl,
		Language = domain.Language,
		ExternalId = domain.ExternalId,
		RetryCount = domain.RetryCount,
		Embedding = domain.Embedding != null ? new Vector(domain.Embedding) : null,
	};
}