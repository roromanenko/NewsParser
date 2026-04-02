using Api.Models;
using Core.DomainModels;

namespace Api.Mappers;

public static class SourceMapper
{
    public static SourceDto ToDto(this Source source) => new(
        source.Id, source.Name, source.Url,
        source.Type.ToString(), source.IsActive, source.LastFetchedAt
    );
}
