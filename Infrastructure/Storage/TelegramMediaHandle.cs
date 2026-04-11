namespace Infrastructure.Storage;

internal static class TelegramMediaHandle
{
    public static string Encode(long channelId, long accessHash, int messageId, int mediaIndex = 0)
        => $"{channelId}:{accessHash}:{messageId}:{mediaIndex}";

    public static bool TryDecode(
        string? handle,
        out long channelId,
        out long accessHash,
        out int messageId,
        out int mediaIndex)
    {
        channelId = 0;
        accessHash = 0;
        messageId = 0;
        mediaIndex = 0;

        if (string.IsNullOrEmpty(handle))
            return false;

        var parts = handle.Split(':');
        if (parts.Length != 4)
            return false;

        if (!long.TryParse(parts[0], out channelId))
            return false;

        if (!long.TryParse(parts[1], out accessHash))
            return false;

        if (!int.TryParse(parts[2], out messageId))
            return false;

        if (!int.TryParse(parts[3], out mediaIndex))
            return false;

        return true;
    }
}
