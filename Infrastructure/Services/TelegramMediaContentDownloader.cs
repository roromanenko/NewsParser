using Core.DomainModels;
using Core.Interfaces.Storage;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class TelegramMediaContentDownloader(
    ITelegramMediaGateway telegramClient,
    ILogger<TelegramMediaContentDownloader> logger) : IMediaContentDownloader
{
    public MediaSourceKind Kind => MediaSourceKind.Telegram;

    public async Task<MediaDownloadResult?> DownloadAsync(
        MediaReference reference,
        CancellationToken cancellationToken = default)
    {
        if (!telegramClient.IsReady)
        {
            logger.LogWarning("Telegram client is not ready; skipping media download for {Url}", reference.Url);
            return null;
        }

        if (reference.ExternalHandle is null)
        {
            logger.LogWarning("MediaReference has no ExternalHandle for Telegram media {Url}", reference.Url);
            return null;
        }

        try
        {
            var destination = new MemoryStream();
            var result = await telegramClient.DownloadMediaAsync(reference.ExternalHandle, destination, cancellationToken);

            if (result is null)
            {
                await destination.DisposeAsync();
                return null;
            }

            destination.Position = 0;
            return new MediaDownloadResult(destination, result.ContentType, result.SizeBytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unexpected error downloading Telegram media {Url}", reference.Url);
            return null;
        }
    }
}
