using Core.DomainModels;
using Core.Interfaces.Parsers;
using Infrastructure.Services;

namespace Infrastructure.Parsers;

public class TelegramParser : ISourceParser
{
	private readonly TelegramClientService _clientService;

	public TelegramParser(TelegramClientService clientService)
	{
		_clientService = clientService;
	}

	public SourceType SourceType => SourceType.Telegram;

	public async Task<List<Article>> ParseAsync(Source source, CancellationToken cancellationToken = default)
	{
		if (!_clientService.IsReady) return [];

		var username = ExtractUsername(source.Url);
		if (string.IsNullOrEmpty(username)) return [];

		var messages = await _clientService.GetChannelMessagesAsync(username, source.Id, cancellationToken);

		return messages.Select(msg => new Article
		{
			Id = Guid.NewGuid(),
			SourceId = source.Id,
			ExternalId = msg.id.ToString(),
			Title = BuildTitle(msg.message),
			OriginalContent = msg.message,
			OriginalUrl = $"https://t.me/{username}/{msg.id}",
			PublishedAt = new DateTimeOffset(msg.date, TimeSpan.Zero),
			Language = string.Empty,
			Status = ArticleStatus.Pending,
			ProcessedAt = DateTimeOffset.UtcNow,
		}).ToList();
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
