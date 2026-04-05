using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Core.Interfaces.AI;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI;

public class HaikuEventTitleGenerator : IEventTitleGenerator
{
	private readonly AnthropicClient _client;
	private readonly string _model;
	private readonly ILogger<HaikuEventTitleGenerator> _logger;

	private const string SystemPrompt =
		"You are a news headline writer. Generate a concise Ukrainian-language news headline. " +
		"Rules: maximum 15 words; no quotes; no trailing punctuation; no prefixes like \"Breaking:\"; " +
		"factual and neutral. Respond with the headline text only — no extra formatting.";

	public HaikuEventTitleGenerator(string apiKey, string model, ILogger<HaikuEventTitleGenerator> logger)
	{
		_client = new AnthropicClient(new APIAuthentication(apiKey));
		_model = model;
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
			System = [new SystemMessage(SystemPrompt)],
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
			_logger.LogWarning(ex, "Failed to generate Ukrainian event title via AI");
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
