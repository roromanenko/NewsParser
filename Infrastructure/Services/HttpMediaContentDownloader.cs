using Core.DomainModels;
using Core.Interfaces.Storage;
using Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class HttpMediaContentDownloader(
    IHttpClientFactory httpClientFactory,
    IOptions<CloudflareR2Options> options,
    ILogger<HttpMediaContentDownloader> logger) : IMediaContentDownloader
{
    private static readonly HashSet<string> AllowedMimeRoots = ["image", "video"];

    private static readonly Dictionary<string, string> ExtensionToMime = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".webp", "image/webp" },
        { ".mp4", "video/mp4" },
        { ".mov", "video/quicktime" },
        { ".avi", "video/x-msvideo" },
        { ".webm", "video/webm" },
    };

    private readonly CloudflareR2Options _options = options.Value;

    public MediaSourceKind Kind => MediaSourceKind.Http;

    public async Task<MediaDownloadResult?> DownloadAsync(
        MediaReference reference,
        CancellationToken cancellationToken = default)
    {
        using var httpClient = httpClientFactory.CreateClient("MediaDownloader");
        using var response = await httpClient.GetAsync(
            reference.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("HTTP {StatusCode} when downloading media {Url}", response.StatusCode, reference.Url);
            return null;
        }

        if (response.Content.Headers.ContentLength.HasValue
            && response.Content.Headers.ContentLength.Value > _options.MaxFileSizeBytes)
        {
            logger.LogWarning("Media {Url} exceeds size limit ({Size} bytes)", reference.Url, response.Content.Headers.ContentLength.Value);
            return null;
        }

        var stream = new MemoryStream();
        await response.Content.CopyToAsync(stream, cancellationToken);

        if (stream.Length > _options.MaxFileSizeBytes)
        {
            await stream.DisposeAsync();
            logger.LogWarning("Media {Url} exceeds size limit after download ({Size} bytes)", reference.Url, stream.Length);
            return null;
        }

        var contentType = ResolveContentType(response, reference);
        if (contentType is null)
        {
            await stream.DisposeAsync();
            logger.LogWarning("Unsupported content type for media {Url}", reference.Url);
            return null;
        }

        stream.Position = 0;
        return new MediaDownloadResult(stream, contentType, stream.Length);
    }

    private string? ResolveContentType(HttpResponseMessage response, MediaReference reference)
    {
        var fromHeader = response.Content.Headers.ContentType?.MediaType;
        if (IsAllowedMimeType(fromHeader))
            return fromHeader;

        if (IsAllowedMimeType(reference.DeclaredContentType))
            return reference.DeclaredContentType;

        var ext = Path.GetExtension(new Uri(reference.Url).AbsolutePath);
        if (!string.IsNullOrEmpty(ext) && ExtensionToMime.TryGetValue(ext, out var mapped))
            return mapped;

        return null;
    }

    private static bool IsAllowedMimeType(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
            return false;

        var root = mimeType.Split('/')[0];
        return AllowedMimeRoots.Contains(root);
    }
}
