using Core.DomainModels;
using Infrastructure.Persistence.Entity;

namespace Infrastructure.Persistence.Mappers;

public static class SourceMapper
{
	public static Source ToDomain(this SourceEntity entity) => new()
	{
		Id = entity.Id,
		Name = entity.Name,
		Url = entity.Url,
		Type = Enum.Parse<SourceType>(entity.Type),
		ProjectId = entity.ProjectId,
		IsActive = entity.IsActive,
		LastFetchedAt = entity.LastFetchedAt
	};

	public static SourceEntity ToEntity(this Source domain) => new()
	{
		Id = domain.Id,
		Name = domain.Name,
		Url = domain.Url,
		Type = domain.Type.ToString(),
		ProjectId = domain.ProjectId,
		IsActive = domain.IsActive,
		LastFetchedAt = domain.LastFetchedAt
	};
}
