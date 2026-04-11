using Api.Models;
using Core.DomainModels;

namespace Api.Mappers;

public static class MediaFileMapper
{
    public static MediaFileDto ToDto(this MediaFile media, string publicBaseUrl) => new(
        media.Id,
        media.ArticleId,
        BuildUrl(publicBaseUrl, media.R2Key),
        media.Kind.ToString(),
        media.ContentType,
        media.SizeBytes
    );

    private static string BuildUrl(string publicBaseUrl, string r2Key)
        => $"{publicBaseUrl.TrimEnd('/')}/{r2Key.TrimStart('/')}";
}
