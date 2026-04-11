using Infrastructure.Models;

namespace Infrastructure.Parsers;

/// <summary>
/// Thin seam that decouples <see cref="TelegramParser"/> from the concrete
/// <see cref="TelegramClientService"/>, enabling unit tests without WTelegram dependencies.
/// Implemented by <c>TelegramClientService</c>; faked in tests without WTelegram references.
/// </summary>
public interface ITelegramChannelReader
{
    bool IsReady { get; }

    Task<List<TelegramChannelMessage>> GetChannelMessagesAsync(
        string username,
        Guid sourceId,
        CancellationToken cancellationToken);
}
