using Core.DomainModels;
using Infrastructure.Persistence.Entity;

namespace Infrastructure.Persistence.Mappers;

public static class MediaFileMapper
{
	public static MediaFile ToDomain(this MediaFileEntity entity) => new()
	{
		Id = entity.Id,
		ArticleId = entity.ArticleId,
		PublicationId = entity.PublicationId,
		OwnerKind = Enum.Parse<MediaOwnerKind>(entity.OwnerKind),
		UploadedByUserId = entity.UploadedByUserId,
		R2Key = entity.R2Key,
		OriginalUrl = entity.OriginalUrl,
		ContentType = entity.ContentType,
		SizeBytes = entity.SizeBytes,
		Kind = Enum.Parse<MediaKind>(entity.Kind),
		CreatedAt = entity.CreatedAt,
	};

	public static MediaFileEntity ToEntity(this MediaFile domain) => new()
	{
		Id = domain.Id,
		ArticleId = domain.ArticleId,
		PublicationId = domain.PublicationId,
		OwnerKind = domain.OwnerKind.ToString(),
		UploadedByUserId = domain.UploadedByUserId,
		R2Key = domain.R2Key,
		OriginalUrl = domain.OriginalUrl,
		ContentType = domain.ContentType,
		SizeBytes = domain.SizeBytes,
		Kind = domain.Kind.ToString(),
		CreatedAt = domain.CreatedAt,
	};
}
