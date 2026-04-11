using TL;

namespace Infrastructure.Models;

public sealed record TelegramChannelMessage(
    Message Message,
    long ChannelId,
    long ChannelAccessHash);
