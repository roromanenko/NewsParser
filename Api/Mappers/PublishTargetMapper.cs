using Api.Models;
using Core.DomainModels;

namespace Api.Mappers;

public static class PublishTargetMapper
{
    public static PublishTargetDto ToDto(this PublishTarget target) => new(
        target.Id,
        target.Name,
        target.Platform.ToString(),
        target.Identifier,
        target.SystemPrompt,
        target.IsActive
    );
}
