using Core.DomainModels;
using Core.Interfaces.Parsers;
using Infrastructure.Models;
using Infrastructure.Storage;
using TL;

namespace Infrastructure.Parsers;

public class TelegramParser(ITelegramChannelReader channelReader) : ISourceParser
{
	public SourceType SourceType => SourceType.Telegram;

	public async Task<List<Article>> ParseAsync(Source source, CancellationToken cancellationToken = default)
	{
		if (!channelReader.IsReady) return [];

		var username = ExtractUsername(source.Url);
		if (string.IsNullOrEmpty(username)) return [];

		var messages = await channelReader.GetChannelMessagesAsync(username, source.Id, cancellationToken);

		return messages.Select(item => new Article
		{
			Id = Guid.NewGuid(),
			SourceId = source.Id,
			ExternalId = item.Message.id.ToString(),
			Title = BuildTitle(item.Message.message),
			OriginalContent = item.Message.message,
			OriginalUrl = $"https://t.me/{username}/{item.Message.id}",
			PublishedAt = new DateTimeOffset(item.Message.date, TimeSpan.Zero),
			Language = string.Empty,
			Status = ArticleStatus.Pending,
			ProcessedAt = DateTimeOffset.UtcNow,
			MediaReferences = ExtractMediaReferences(item, username),
		}).ToList();
	}

	private static List<MediaReference> ExtractMediaReferences(TelegramChannelMessage item, string username)
	{
		var msg = item.Message;
		var baseUrl = $"https://t.me/{username}/{msg.id}";

		return msg.media switch
		{
			MessageMediaPhoto => [new MediaReference(
				Url: $"{baseUrl}#media-0",
				Kind: MediaKind.Image,
				DeclaredContentType: "image/jpeg",
				SourceKind: MediaSourceKind.Telegram,
				ExternalHandle: TelegramMediaHandle.Encode(item.ChannelId, item.ChannelAccessHash, msg.id, 0))],

			MessageMediaDocument { document: Document doc } when doc.mime_type.StartsWith("image/") =>
			[new MediaReference(
				Url: $"{baseUrl}#media-0",
				Kind: MediaKind.Image,
				DeclaredContentType: doc.mime_type,
				SourceKind: MediaSourceKind.Telegram,
				ExternalHandle: TelegramMediaHandle.Encode(item.ChannelId, item.ChannelAccessHash, msg.id, 0))],

			MessageMediaDocument { document: Document doc } when doc.mime_type.StartsWith("video/") =>
			[new MediaReference(
				Url: $"{baseUrl}#media-0",
				Kind: MediaKind.Video,
				DeclaredContentType: doc.mime_type,
				SourceKind: MediaSourceKind.Telegram,
				ExternalHandle: TelegramMediaHandle.Encode(item.ChannelId, item.ChannelAccessHash, msg.id, 0))],

			_ => []
		};
	}

	private static string ExtractUsername(string url)
	{
		url = url.Trim();
		if (url.StartsWith("https://t.me/", StringComparison.OrdinalIgnoreCase))
			return url["https://t.me/".Length..].Split('/')[0];
		if (url.StartsWith("t.me/", StringComparison.OrdinalIgnoreCase))
			return url["t.me/".Length..].Split('/')[0];
		if (url.StartsWith('@'))
			return url[1..];
		return url.Split('/')[0];
	}

	private static string BuildTitle(string message)
	{
		if (string.IsNullOrEmpty(message)) return string.Empty;
		var firstLine = message.Split('\n')[0];
		return firstLine.Length > 100 ? firstLine[..100] : firstLine;
	}
}
