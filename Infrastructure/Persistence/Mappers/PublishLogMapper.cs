using Core.DomainModels;
using Infrastructure.Persistence.Entity;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Mappers;

public static class PublishLogMapper
{
	public static PublishLog ToDomain(this PublishLogEntity entity) => new()
	{
		Id = entity.Id,
		PublicationId = entity.PublicationId,
		Status = Enum.Parse<PublishLogStatus>(entity.Status),
		ErrorMessage = entity.ErrorMessage,
		AttemptedAt = entity.AttemptedAt,
		ExternalMessageId = entity.ExternalMessageId,
	};

	public static PublishLogEntity ToEntity(this PublishLog domain) => new()
	{
		Id = domain.Id,
		PublicationId = domain.PublicationId,
		Status = domain.Status.ToString(),
		ErrorMessage = domain.ErrorMessage,
		AttemptedAt = domain.AttemptedAt,
		ExternalMessageId = domain.ExternalMessageId,
	};
}