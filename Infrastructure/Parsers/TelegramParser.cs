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

		var (albums, singles) = GroupMessages(messages);

		var articles = new List<Article>(albums.Count + singles.Count);

		foreach (var albumGroup in albums)
			articles.Add(BuildAlbumArticle(albumGroup, username, source.Id));

		foreach (var single in singles)
			articles.Add(BuildSingleArticle(single, username, source.Id));

		// Preserve chronological order (Telegram returns messages newest-first by ID)
		articles.Sort((a, b) => int.Parse(b.ExternalId).CompareTo(int.Parse(a.ExternalId)));

		return articles;
	}

	private static (List<IGrouping<long, TelegramChannelMessage>> albums, List<TelegramChannelMessage> singles)
		GroupMessages(List<TelegramChannelMessage> messages)
	{
		var albums = messages
			.Where(m => m.Message.grouped_id != 0)
			.GroupBy(m => m.Message.grouped_id)
			.ToList();

		var singles = messages
			.Where(m => m.Message.grouped_id == 0)
			.ToList();

		return (albums, singles);
	}

	private static Article BuildAlbumArticle(IGrouping<long, TelegramChannelMessage> group, string username, Guid sourceId)
	{
		var sorted = group.OrderBy(m => m.Message.id).ToList();

		var primary = sorted.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Message.message))
			?? sorted.First();

		var mediaReferences = sorted
			.SelectMany((msg, index) => ExtractMediaReferences(msg, username, mediaUrlIndex: index))
			.ToList();

		return new Article
		{
			Id = Guid.NewGuid(),
			SourceId = sourceId,
			ExternalId = primary.Message.id.ToString(),
			Title = BuildTitle(primary.Message.message),
			OriginalContent = primary.Message.message,
			OriginalUrl = $"https://t.me/{username}/{primary.Message.id}",
			PublishedAt = new DateTimeOffset(primary.Message.date, TimeSpan.Zero),
			Language = string.Empty,
			Status = ArticleStatus.Pending,
			ProcessedAt = DateTimeOffset.UtcNow,
			MediaReferences = mediaReferences,
		};
	}

	private static Article BuildSingleArticle(TelegramChannelMessage item, string username, Guid sourceId)
	{
		return new Article
		{
			Id = Guid.NewGuid(),
			SourceId = sourceId,
			ExternalId = item.Message.id.ToString(),
			Title = BuildTitle(item.Message.message),
			OriginalContent = item.Message.message,
			OriginalUrl = $"https://t.me/{username}/{item.Message.id}",
			PublishedAt = new DateTimeOffset(item.Message.date, TimeSpan.Zero),
			Language = string.Empty,
			Status = ArticleStatus.Pending,
			ProcessedAt = DateTimeOffset.UtcNow,
			MediaReferences = ExtractMediaReferences(item, username, mediaUrlIndex: 0),
		};
	}

	private static List<MediaReference> ExtractMediaReferences(TelegramChannelMessage item, string username, int mediaUrlIndex)
	{
		var msg = item.Message;
		var baseUrl = $"https://t.me/{username}/{msg.id}";

		return msg.media switch
		{
			MessageMediaPhoto => [new MediaReference(
				Url: $"{baseUrl}#media-{mediaUrlIndex}",
				Kind: MediaKind.Image,
				DeclaredContentType: "image/jpeg",
				SourceKind: MediaSourceKind.Telegram,
				ExternalHandle: TelegramMediaHandle.Encode(item.ChannelId, item.ChannelAccessHash, msg.id, 0))],

			MessageMediaDocument { document: Document doc } when doc.mime_type.StartsWith("image/") =>
			[new MediaReference(
				Url: $"{baseUrl}#media-{mediaUrlIndex}",
				Kind: MediaKind.Image,
				DeclaredContentType: doc.mime_type,
				SourceKind: MediaSourceKind.Telegram,
				ExternalHandle: TelegramMediaHandle.Encode(item.ChannelId, item.ChannelAccessHash, msg.id, 0))],

			MessageMediaDocument { document: Document doc } when doc.mime_type.StartsWith("video/") =>
			[new MediaReference(
				Url: $"{baseUrl}#media-{mediaUrlIndex}",
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
