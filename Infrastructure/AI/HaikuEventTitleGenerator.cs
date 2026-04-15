using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Core.Interfaces.AI;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI;

public class HaikuEventTitleGenerator : IEventTitleGenerator
{
	private readonly AnthropicClient _client;
	private readonly string _model;
	private readonly string _systemPrompt;
	private readonly ILogger<HaikuEventTitleGenerator> _logger;

	public HaikuEventTitleGenerator(
		string apiKey,
		string model,
		string systemPrompt,
		ILogger<HaikuEventTitleGenerator> logger)
	{
		_client = new AnthropicClient(new APIAuthentication(apiKey));
		_model = model;
		_systemPrompt = systemPrompt;
		_logger = logger;
	}

	public async Task<string> GenerateTitleAsync(
		string eventSummary,
		List<string> articleTitles,
		CancellationToken cancellationToken = default)
	{
		var userMessage = BuildUserMessage(eventSummary, articleTitles);

		var request = new MessageParameters
		{
			Model = _model,
			MaxTokens = 128,
			System = [new SystemMessage(_systemPrompt)],
			Messages = [new Message(RoleType.User, userMessage)]
		};

		try
		{
			var response = await _client.Messages.GetClaudeMessageAsync(request, cancellationToken);
			var raw = response.Content.FirstOrDefault()?.ToString() ?? string.Empty;
			return raw.Trim();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Failed to generate event title via AI");
			return string.Empty;
		}
	}

	private static string BuildUserMessage(string eventSummary, List<string> articleTitles)
	{
		var titles = string.Join("\n", articleTitles.Select(t => $"- {t}"));
		return $"""
            SUMMARY:
            {eventSummary}

            ARTICLE TITLES:
            {titles}
            """;
	}
}
