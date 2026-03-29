using Core.DomainModels;
using Core.Interfaces.Publishers;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace Infrastructure.Publishers;

public class TelegramPublisher : IPublisher
{
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
		CancellationToken cancellationToken = default)
	{
		return await SendMessageAsync(
			publication.PublishTarget.Identifier,
			publication.GeneratedContent,
			replyToMessageId: null,
			cancellationToken);
	}

	public async Task<string> PublishReplyAsync(
		Publication publication,
		string replyToMessageId,
		CancellationToken cancellationToken = default)
	{
		return await SendMessageAsync(
			publication.PublishTarget.Identifier,
			publication.GeneratedContent,
			replyToMessageId,
			cancellationToken);
	}

	private async Task<string> SendMessageAsync(
		string channelId,
		string text,
		string? replyToMessageId,
		CancellationToken cancellationToken)
	{
		var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";

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
}