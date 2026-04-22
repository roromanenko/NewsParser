using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Core.Interfaces.Storage;
using Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class MediaIngestionService(
    IMediaStorage storage,
    IMediaFileRepository repository,
    IEnumerable<IMediaContentDownloader> downloaders,
    IOptions<CloudflareR2Options> options,
    ILogger<MediaIngestionService> logger) : IMediaIngestionService
{
    private readonly CloudflareR2Options _options = options.Value;
    private readonly Dictionary<MediaSourceKind, IMediaContentDownloader> _downloaders =
        downloaders.ToDictionary(d => d.Kind);

    public async Task IngestForArticleAsync(
        Guid articleId,
        IReadOnlyList<MediaReference> references,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (references.Count == 0)
                return;

            var uniqueReferences = references
                .GroupBy(r => r.Url, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            foreach (var reference in uniqueReferences)
            {
                try
                {
                    await IngestSingleReferenceAsync(articleId, reference, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Media ingestion failed for URL {Url} on article {ArticleId}", reference.Url, articleId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unexpected error during media ingestion for article {ArticleId}", articleId);
        }
    }

    private async Task IngestSingleReferenceAsync(
        Guid articleId,
        MediaReference reference,
        CancellationToken cancellationToken)
    {
        var alreadyExists = await repository.ExistsByArticleAndUrlAsync(articleId, reference.Url, cancellationToken);
        if (alreadyExists)
            return;

        if (!_downloaders.TryGetValue(reference.SourceKind, out var downloader))
        {
            logger.LogWarning("No downloader registered for media source kind {Kind}", reference.SourceKind);
            return;
        }

        var download = await downloader.DownloadAsync(reference, cancellationToken);
        if (download is null)
            return;

        try
        {
            if (download.SizeBytes > _options.MaxFileSizeBytes)
            {
                logger.LogWarning("Media {Url} exceeds size limit ({Size} bytes)", reference.Url, download.SizeBytes);
                return;
            }

            var kind = download.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                ? MediaKind.Video
                : MediaKind.Image;

            var ext = ResolveExtension(reference, download.ContentType);
            var r2Key = BuildR2Key(articleId, ext);

            download.Content.Position = 0;
            await storage.UploadAsync(r2Key, download.Content, download.ContentType, cancellationToken);

            var mediaFile = new MediaFile
            {
                Id = Guid.NewGuid(),
                ArticleId = articleId,
                OwnerKind = MediaOwnerKind.Article,
                PublicationId = null,
                UploadedByUserId = null,
                R2Key = r2Key,
                OriginalUrl = reference.Url,
                ContentType = download.ContentType,
                SizeBytes = download.SizeBytes,
                Kind = kind,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            await repository.AddAsync(mediaFile, cancellationToken);
            logger.LogInformation("Ingested media {Url} for article {ArticleId} as {R2Key}", reference.Url, articleId, r2Key);
        }
        finally
        {
            await download.Content.DisposeAsync();
        }
    }

    private static string BuildR2Key(Guid articleId, string ext)
        => $"articles/{articleId}/{Guid.NewGuid()}{ext}";

    private static string ResolveExtension(MediaReference reference, string contentType)
    {
        if (!string.IsNullOrEmpty(reference.Url))
        {
            try
            {
                var ext = Path.GetExtension(new Uri(reference.Url).AbsolutePath);
                if (!string.IsNullOrEmpty(ext))
                    return ext;
            }
            catch (UriFormatException)
            {
                // Fall through to content-type-based extension
            }
        }

        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "video/quicktime" => ".mov",
            "video/webm" => ".webm",
            _ => string.Empty,
        };
    }
}
