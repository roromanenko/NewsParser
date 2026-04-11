using Core.DomainModels;

namespace Core.Interfaces.Storage;

public interface IMediaContentDownloader
{
    MediaSourceKind Kind { get; }

    Task<MediaDownloadResult?> DownloadAsync(
        MediaReference reference,
        CancellationToken cancellationToken = default);
}

public sealed record MediaDownloadResult(
    Stream Content,
    string ContentType,
    long SizeBytes);
