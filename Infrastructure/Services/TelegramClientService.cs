using System.Collections.Concurrent;
using Core.Interfaces.Storage;
using Infrastructure.Configuration;
using Infrastructure.Models;
using Infrastructure.Parsers;
using Infrastructure.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TL;

namespace Infrastructure.Services;

public class TelegramClientService : IHostedService, IAsyncDisposable, ITelegramChannelReader, ITelegramMediaGateway
{
	private WTelegram.Client? _client;
	private FileStream? _sessionStream;
	private volatile bool _isReady;
	private readonly ConcurrentDictionary<Guid, int> _lastMessageIds = new();
	private readonly TelegramOptions _options;
	private readonly ILogger<TelegramClientService> _logger;

	public bool IsReady => _isReady;

	public TelegramClientService(IOptions<TelegramOptions> options, ILogger<TelegramClientService> logger)
	{
		_options = options.Value;
		_logger = logger;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_ = Task.Run(InitializeAsync, CancellationToken.None);
		return Task.CompletedTask;
	}

	private async Task InitializeAsync()
	{
		try
		{
			_sessionStream = File.Open(_options.SessionFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
			_client = new WTelegram.Client(Config, _sessionStream);
			await _client.LoginUserIfNeeded();
			_isReady = true;
			_logger.LogInformation("Telegram client connected successfully");
		}
		catch (Exception ex)
		{
			_logger.LogCritical(ex, "Telegram client failed to initialize. Run Tools/TelegramAuth to authenticate first.");
		}
	}

	private string? Config(string what) => what switch
	{
		"api_id" => _options.ApiId.ToString(),
		"api_hash" => _options.ApiHash,
		"phone_number" => _options.PhoneNumber,
		_ => null
	};

	public async Task<List<TelegramChannelMessage>> GetChannelMessagesAsync(string username, Guid sourceId, CancellationToken cancellationToken)
	{
		if (_client is null) return [];

		var resolved = await _client.Contacts_ResolveUsername(username);
		if (resolved.Chat is not Channel channel)
		{
			_logger.LogWarning("Could not resolve Telegram channel: {Username}", username);
			return [];
		}

		_lastMessageIds.TryGetValue(sourceId, out var minId);
		var history = await _client.Messages_GetHistory(channel.ToInputPeer(), min_id: minId, limit: 100);

		var messages = history.Messages
			.OfType<Message>()
			// Album items carry media but no caption — allow them through for grouping in TelegramParser
			.Where(m => !string.IsNullOrWhiteSpace(m.message) || m.media is not null)
			.Select(m => new TelegramChannelMessage(m, channel.id, channel.access_hash))
			.ToList();

		if (messages.Count > 0)
			_lastMessageIds[sourceId] = messages.Max(m => m.Message.id);

		return messages;
	}

	public async Task<TelegramMediaDownloadResult?> DownloadMediaAsync(
		string externalHandle,
		Stream destination,
		CancellationToken cancellationToken = default)
	{
		if (_client is null) return null;

		if (!TelegramMediaHandle.TryDecode(externalHandle, out var channelId, out var accessHash, out var messageId, out _))
			return null;

		try
		{
			var inputChannel = new InputChannel(channelId, accessHash);
			var result = await _client.Channels_GetMessages(inputChannel, new InputMessageID { id = messageId });
			var message = result.Messages.OfType<Message>().FirstOrDefault();
			if (message is null) return null;

			return message.media switch
			{
				MessageMediaPhoto { photo: Photo p } =>
					await DownloadPhotoAsync(p, destination),

				MessageMediaDocument { document: Document d } when d.mime_type.StartsWith("image/") || d.mime_type.StartsWith("video/") =>
					await DownloadDocumentAsync(d, destination),

				_ => null
			};
		}
		catch (WTelegram.WTException ex)
		{
			_logger.LogWarning(ex, "Telegram media download failed for handle {Handle}", externalHandle);
			return null;
		}
	}

	private async Task<TelegramMediaDownloadResult> DownloadPhotoAsync(Photo photo, Stream destination)
	{
		await _client!.DownloadFileAsync(photo, destination);
		return new TelegramMediaDownloadResult("image/jpeg", destination.Length);
	}

	private async Task<TelegramMediaDownloadResult> DownloadDocumentAsync(Document document, Stream destination)
	{
		await _client!.DownloadFileAsync(document, destination);
		return new TelegramMediaDownloadResult(document.mime_type, document.size);
	}

	public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

	public async ValueTask DisposeAsync()
	{
		if (_client is not null)
		{
			_client.Dispose();
			_client = null;
		}
		if (_sessionStream is not null)
		{
			await _sessionStream.DisposeAsync();
			_sessionStream = null;
		}
	}
}
