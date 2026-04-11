namespace Core.Interfaces.Storage;

/// <summary>
/// Thin seam that decouples <see cref="Infrastructure.Services.TelegramMediaContentDownloader"/>
/// from the concrete <see cref="Infrastructure.Services.TelegramClientService"/>.
/// Implemented by <c>TelegramClientService</c>; faked in tests without WTelegram dependencies.
/// </summary>
public interface ITelegramMediaGateway
{
    bool IsReady { get; }

    Task<TelegramMediaDownloadResult?> DownloadMediaAsync(
        string externalHandle,
        Stream destination,
        CancellationToken cancellationToken = default);
}

/// <summary>Payload returned by a successful Telegram media download.</summary>
public sealed record TelegramMediaDownloadResult(string ContentType, long SizeBytes);
