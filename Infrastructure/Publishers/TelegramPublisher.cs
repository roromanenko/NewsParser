using Core.DomainModels;
using Core.Interfaces.Publishers;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace Infrastructure.Publishers;

public class TelegramPublisher : IPublisher
{
	private const int TelegramCaptionMaxLength = 1024;
	private const int TelegramMediaGroupMaxSize = 10;
	private const long TelegramPhotoMaxSizeBytes = 20 * 1024 * 1024;

	private readonly string _botToken;
	private readonly ILogger<TelegramPublisher> _logger;
	private readonly HttpClient _httpClient;

	public Platform Platform => Platform.Telegram;

	public TelegramPublisher(string botToken, ILogger<TelegramPublisher> logger, HttpClient httpClient)
	{
		_botToken = botToken;
		_logger = logger;
		_httpClient = httpClient;
	}

	public async Task<string> PublishAsync(
		Publication publication,
		List<ResolvedMedia> media,
		CancellationToken cancellationToken = default)
	{
		return await DispatchAsync(
			publication.PublishTarget.Identifier,
			publication.GeneratedContent,
			media,
			replyToMessageId: null,
			cancellationToken);
	}

	public async Task<string> PublishReplyAsync(
		Publication publication,
		string replyToMessageId,
		List<ResolvedMedia> media,
		CancellationToken cancellationToken = default)
	{
		return await DispatchAsync(
			publication.PublishTarget.Identifier,
			publication.GeneratedContent,
			media,
			replyToMessageId,
			cancellationToken);
	}

	private async Task<string> DispatchAsync(
		string channelId,
		string content,
		List<ResolvedMedia> media,
		string? replyToMessageId,
		CancellationToken cancellationToken)
	{
		if (media.Count == 0)
			return await SendMessageAsync(channelId, content, replyToMessageId, cancellationToken);

		if (media.Count == 1 && media[0].Kind == MediaKind.Image)
			return await SendPhotoAsync(channelId, media[0].Url, content, replyToMessageId, cancellationToken);

		if (media.Count == 1 && media[0].Kind == MediaKind.Video)
			return await SendVideoAsync(channelId, media[0].Url, content, replyToMessageId, cancellationToken);

		var group = media.Count > TelegramMediaGroupMaxSize
			? media.Take(TelegramMediaGroupMaxSize).ToList()
			: media;

		return await SendMediaGroupAsync(channelId, group, content, replyToMessageId, cancellationToken);
	}

	private async Task<string> SendPhotoAsync(
		string channelId,
		string photoUrl,
		string caption,
		string? replyToMessageId,
		CancellationToken cancellationToken)
	{
		if (caption.Length <= TelegramCaptionMaxLength)
		{
			var url = BuildApiUrl("sendPhoto");
			var payload = BuildPhotoPayload(channelId, photoUrl, caption, replyToMessageId);
			return await PostAndReadMessageIdAsync(url, payload, channelId, cancellationToken);
		}

		var urlCaptionless = BuildApiUrl("sendPhoto");
		var photoCaptionless = BuildPhotoPayload(channelId, photoUrl, caption: null, replyToMessageId);
		var photoMessageId = await PostAndReadMessageIdAsync(urlCaptionless, photoCaptionless, channelId, cancellationToken);
		return await SendMessageAsync(channelId, caption, replyToMessageId: photoMessageId, cancellationToken);
	}

	private async Task<string> SendVideoAsync(
		string channelId,
		string videoUrl,
		string caption,
		string? replyToMessageId,
		CancellationToken cancellationToken)
	{
		if (caption.Length <= TelegramCaptionMaxLength)
		{
			var url = BuildApiUrl("sendVideo");
			var payload = BuildVideoPayload(channelId, videoUrl, caption, replyToMessageId);
			return await PostAndReadMessageIdAsync(url, payload, channelId, cancellationToken);
		}

		var urlCaptionless = BuildApiUrl("sendVideo");
		var videoCaptionless = BuildVideoPayload(channelId, videoUrl, caption: null, replyToMessageId);
		var videoMessageId = await PostAndReadMessageIdAsync(urlCaptionless, videoCaptionless, channelId, cancellationToken);
		return await SendMessageAsync(channelId, caption, replyToMessageId: videoMessageId, cancellationToken);
	}

	private async Task<string> SendMediaGroupAsync(
		string channelId,
		List<ResolvedMedia> media,
		string caption,
		string? replyToMessageId,
		CancellationToken cancellationToken)
	{
		var effectiveCaption = caption.Length <= TelegramCaptionMaxLength ? caption : null;
		var url = BuildApiUrl("sendMediaGroup");
		var payload = BuildMediaGroupPayload(channelId, media, effectiveCaption, replyToMessageId);
		var firstMessageId = await PostAndReadFirstMessageIdAsync(url, payload, channelId, cancellationToken);

		if (effectiveCaption is null)
			return await SendMessageAsync(channelId, caption, replyToMessageId: firstMessageId, cancellationToken);

		return firstMessageId;
	}

	private async Task<string> SendMessageAsync(
		string channelId,
		string text,
		string? replyToMessageId,
		CancellationToken cancellationToken)
	{
		var url = BuildApiUrl("sendMessage");

		object payload = replyToMessageId is not null
			? new
			{
				chat_id = channelId,
				text,
				parse_mode = "MarkdownV2",
				link_preview_options = new { is_disabled = true },
				reply_parameters = new { message_id = int.Parse(replyToMessageId) }
			}
			: new
			{
				chat_id = channelId,
				text,
				parse_mode = "MarkdownV2",
				link_preview_options = new { is_disabled = true }
			};

		return await PostAndReadMessageIdAsync(url, payload, channelId, cancellationToken);
	}

	private async Task<string> PostAndReadMessageIdAsync(
		string url,
		object payload,
		string channelId,
		CancellationToken cancellationToken)
	{
		var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			var error = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new InvalidOperationException(
				$"Telegram API error for channel {channelId}: {response.StatusCode} — {error}");
		}

		var json = await response.Content.ReadAsStringAsync(cancellationToken);
		using var doc = JsonDocument.Parse(json);
		var messageId = doc.RootElement
			.GetProperty("result")
			.GetProperty("message_id")
			.GetInt32()
			.ToString();

		_logger.LogInformation(
			"Successfully published to Telegram channel {ChannelId}, message_id: {MessageId}",
			channelId, messageId);

		return messageId;
	}

	private async Task<string> PostAndReadFirstMessageIdAsync(
		string url,
		object payload,
		string channelId,
		CancellationToken cancellationToken)
	{
		var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			var error = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new InvalidOperationException(
				$"Telegram API error for channel {channelId}: {response.StatusCode} — {error}");
		}

		var json = await response.Content.ReadAsStringAsync(cancellationToken);
		using var doc = JsonDocument.Parse(json);
		var messageId = doc.RootElement
			.GetProperty("result")[0]
			.GetProperty("message_id")
			.GetInt32()
			.ToString();

		_logger.LogInformation(
			"Successfully published media group to Telegram channel {ChannelId}, first message_id: {MessageId}",
			channelId, messageId);

		return messageId;
	}

	private string BuildApiUrl(string method) =>
		$"https://api.telegram.org/bot{_botToken}/{method}";

	private static object BuildPhotoPayload(
		string channelId,
		string photoUrl,
		string? caption,
		string? replyToMessageId)
	{
		return replyToMessageId is not null
			? new { chat_id = channelId, photo = photoUrl, caption, parse_mode = "MarkdownV2", reply_parameters = new { message_id = int.Parse(replyToMessageId) } }
			: new { chat_id = channelId, photo = photoUrl, caption, parse_mode = "MarkdownV2" };
	}

	private static object BuildVideoPayload(
		string channelId,
		string videoUrl,
		string? caption,
		string? replyToMessageId)
	{
		return replyToMessageId is not null
			? new { chat_id = channelId, video = videoUrl, caption, parse_mode = "MarkdownV2", reply_parameters = new { message_id = int.Parse(replyToMessageId) } }
			: new { chat_id = channelId, video = videoUrl, caption, parse_mode = "MarkdownV2" };
	}

	private static object BuildMediaGroupPayload(
		string channelId,
		List<ResolvedMedia> media,
		string? caption,
		string? replyToMessageId)
	{
		var inputMedia = media.Select((m, index) => BuildInputMediaItem(m, index == 0 ? caption : null)).ToArray();

		return replyToMessageId is not null
			? new { chat_id = channelId, media = inputMedia, reply_parameters = new { message_id = int.Parse(replyToMessageId) } }
			: new { chat_id = channelId, media = inputMedia };
	}

	private static object BuildInputMediaItem(ResolvedMedia mediaItem, string? caption) =>
		(mediaItem.Kind, caption) switch
		{
			(MediaKind.Image, not null) => new { type = "photo", media = mediaItem.Url, caption, parse_mode = "MarkdownV2" },
			(MediaKind.Image, null)     => (object)new { type = "photo", media = mediaItem.Url, parse_mode = "MarkdownV2" },
			(_, not null)               => new { type = "video", media = mediaItem.Url, caption, parse_mode = "MarkdownV2" },
			_                           => new { type = "video", media = mediaItem.Url, parse_mode = "MarkdownV2" },
		};
}
