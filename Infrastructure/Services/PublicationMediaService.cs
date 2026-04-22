using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Core.Interfaces.Storage;
using Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class PublicationMediaService(
    IPublicationRepository publicationRepository,
    IMediaFileRepository mediaFileRepository,
    IMediaStorage storage,
    IOptions<PublicationMediaOptions> options,
    ILogger<PublicationMediaService> logger) : IPublicationMediaService
{
    private readonly PublicationMediaOptions _options = options.Value;

    private static readonly PublicationStatus[] MutableStatuses =
    [
        PublicationStatus.Created,
        PublicationStatus.ContentReady,
        PublicationStatus.Failed
    ];

    public async Task<MediaFile> UploadAsync(
        Guid publicationId,
        Guid uploadedByUserId,
        Stream content,
        string fileName,
        string contentType,
        long sizeBytes,
        CancellationToken cancellationToken = default)
    {
        var publication = await publicationRepository.GetByIdAsync(publicationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Publication {publicationId} not found");

        GuardMutableStatus(publication);
        GuardFileSize(sizeBytes);

        var resolvedContentType = ResolveContentType(contentType, fileName);
        GuardContentType(resolvedContentType);

        await GuardPublicationCountAsync(publicationId, cancellationToken);

        var derivedKind = DeriveMediaKind(resolvedContentType);
        var ext = ExtensionFromContentType(resolvedContentType);
        var mediaId = Guid.NewGuid();
        var r2Key = $"publications/{publicationId}/{mediaId}{ext}";

        content.Position = 0;
        await storage.UploadAsync(r2Key, content, resolvedContentType, cancellationToken);

        var mediaFile = await PersistMediaFileAsync(
            mediaId, publicationId, uploadedByUserId, r2Key, resolvedContentType, sizeBytes, derivedKind, cancellationToken);

        logger.LogInformation(
            "Custom media {MediaId} uploaded for publication {PublicationId} by {UserId} ({SizeBytes} bytes, {ContentType})",
            mediaId, publicationId, uploadedByUserId, sizeBytes, resolvedContentType);

        return mediaFile;
    }

    public async Task DeleteAsync(
        Guid publicationId,
        Guid mediaFileId,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        var media = await mediaFileRepository.GetByIdAsync(mediaFileId, cancellationToken)
            ?? throw new KeyNotFoundException($"Media file {mediaFileId} not found");

        if (media.OwnerKind != MediaOwnerKind.Publication || media.PublicationId != publicationId)
            throw new InvalidOperationException("Cannot delete this media via the publication endpoint");

        var publication = await publicationRepository.GetByIdAsync(publicationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Publication {publicationId} not found");

        GuardMutableStatus(publication);

        await TryDeleteFromStorageAsync(media.R2Key, cancellationToken);
        await mediaFileRepository.DeleteAsync(media.Id, cancellationToken);
        await StripFromSelectedMediaIfPresentAsync(publication, media.Id, cancellationToken);

        logger.LogInformation(
            "Custom media {MediaId} deleted from publication {PublicationId} by {UserId}",
            mediaFileId, publicationId, requestedByUserId);
    }

    private void GuardMutableStatus(Publication publication)
    {
        if (!MutableStatuses.Contains(publication.Status))
            throw new InvalidOperationException(
                $"Publication {publication.Id} is not in a mutable status (current: {publication.Status})");
    }

    private void GuardFileSize(long sizeBytes)
    {
        if (sizeBytes > _options.MaxUploadBytes)
            throw new ArgumentException(
                $"File size {sizeBytes} bytes exceeds the allowed maximum of {_options.MaxUploadBytes} bytes");
    }

    private string ResolveContentType(string contentType, string fileName)
    {
        if (!string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return contentType;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            _ => contentType
        };
    }

    private void GuardContentType(string contentType)
    {
        if (!_options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Content type '{contentType}' is not allowed. Allowed types: {string.Join(", ", _options.AllowedContentTypes)}");
    }

    private async Task GuardPublicationCountAsync(Guid publicationId, CancellationToken cancellationToken)
    {
        var existing = await mediaFileRepository.GetByPublicationIdAsync(publicationId, cancellationToken);
        if (existing.Count >= _options.MaxFilesPerPublication)
            throw new InvalidOperationException(
                $"Publication {publicationId} already has the maximum of {_options.MaxFilesPerPublication} custom media files");
    }

    private static MediaKind DeriveMediaKind(string contentType) =>
        contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            ? MediaKind.Video
            : MediaKind.Image;

    private static string ExtensionFromContentType(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "video/mp4" => ".mp4",
            _ => string.Empty
        };

    private async Task<MediaFile> PersistMediaFileAsync(
        Guid mediaId,
        Guid publicationId,
        Guid uploadedByUserId,
        string r2Key,
        string contentType,
        long sizeBytes,
        MediaKind kind,
        CancellationToken cancellationToken)
    {
        var mediaFile = new MediaFile
        {
            Id = mediaId,
            OwnerKind = MediaOwnerKind.Publication,
            PublicationId = publicationId,
            UploadedByUserId = uploadedByUserId,
            ArticleId = null,
            OriginalUrl = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            R2Key = r2Key,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Kind = kind
        };

        try
        {
            await mediaFileRepository.AddAsync(mediaFile, cancellationToken);
        }
        catch
        {
            await storage.DeleteAsync(r2Key, CancellationToken.None);
            throw;
        }

        return mediaFile;
    }

    private async Task TryDeleteFromStorageAsync(string r2Key, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(r2Key, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete R2 object {R2Key}; DB row will still be removed", r2Key);
        }
    }

    private async Task StripFromSelectedMediaIfPresentAsync(
        Publication publication,
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        if (!publication.SelectedMediaFileIds.Contains(mediaId))
            return;

        var updatedIds = publication.SelectedMediaFileIds.Where(id => id != mediaId).ToList();
        await publicationRepository.UpdateContentAndMediaAsync(
            publication.Id, publication.GeneratedContent ?? string.Empty, updatedIds, cancellationToken);
    }
}
