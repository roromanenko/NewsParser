using Core.DomainModels;
using Infrastructure.Persistence.Entity;

namespace Infrastructure.Persistence.Mappers;

public static class PublishTargetMapper
{
	public static PublishTarget ToDomain(this PublishTargetEntity entity) => new()
	{
		Id = entity.Id,
		Name = entity.Name,
		Platform = Enum.Parse<Platform>(entity.Platform),
		Identifier = entity.Identifier,
		SystemPrompt = entity.SystemPrompt,
		IsActive = entity.IsActive,
	};

	public static PublishTargetEntity ToEntity(this PublishTarget domain) => new()
	{
		Id = domain.Id,
		Name = domain.Name,
		Platform = domain.Platform.ToString(),
		Identifier = domain.Identifier,
		SystemPrompt = domain.SystemPrompt,
		IsActive = domain.IsActive,
	};
}