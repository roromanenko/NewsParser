using System.Collections.Concurrent;
using Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TL;

namespace Infrastructure.Services;

public class TelegramClientService : IHostedService, IAsyncDisposable
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
			_logger.LogError(ex, "Telegram client failed to initialize. Run Tools/TelegramAuth to authenticate first.");
		}
	}

	private string? Config(string what) => what switch
	{
		"api_id" => _options.ApiId.ToString(),
		"api_hash" => _options.ApiHash,
		"phone_number" => _options.PhoneNumber,
		_ => null
	};

	public async Task<List<Message>> GetChannelMessagesAsync(string username, Guid sourceId, CancellationToken cancellationToken)
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
			.Where(m => !string.IsNullOrWhiteSpace(m.message))
			.ToList();

		if (messages.Count > 0)
			_lastMessageIds[sourceId] = messages.Max(m => m.id);

		return messages;
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
