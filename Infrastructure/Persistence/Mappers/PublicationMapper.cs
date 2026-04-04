using Core.DomainModels;
using Infrastructure.Persistence.Entity;

namespace Infrastructure.Persistence.Mappers;

public static class PublicationMapper
{
	public static Publication ToDomain(this PublicationEntity entity) => new()
	{
		Id = entity.Id,
		Article = entity.Article?.ToDomain() ?? new Article(),
		PublishTargetId = entity.PublishTargetId,
		PublishTarget = entity.PublishTarget?.ToDomain() ?? new PublishTarget(),
		GeneratedContent = entity.GeneratedContent,
		Status = Enum.Parse<PublicationStatus>(entity.Status),
		CreatedAt = entity.CreatedAt,
		PublishedAt = entity.PublishedAt,
		ApprovedAt = entity.ApprovedAt,
		EventId = entity.EventId,
		Event = entity.Event?.ToDomain(),
		ParentPublicationId = entity.ParentPublicationId,
		UpdateContext = entity.UpdateContext,
	};

	public static PublicationEntity ToEntity(this Publication domain, Guid articleId, Guid? editorId = null) => new()
	{
		Id = domain.Id,
		ArticleId = articleId,
		EditorId = editorId == Guid.Empty ? null : editorId,
		PublishTargetId = domain.PublishTargetId,
		GeneratedContent = domain.GeneratedContent,
		Status = domain.Status.ToString(),
		CreatedAt = domain.CreatedAt,
		PublishedAt = domain.PublishedAt,
		ApprovedAt = domain.ApprovedAt,
		EventId = domain.EventId,
		ParentPublicationId = domain.ParentPublicationId,
		UpdateContext = domain.UpdateContext,
	};
}