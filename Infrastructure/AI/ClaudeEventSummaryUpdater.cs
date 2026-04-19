using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Infrastructure.AI;

public class ClaudeEventSummaryUpdater : IEventSummaryUpdater
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly string _prompt;
	private readonly ILogger<ClaudeEventSummaryUpdater> _logger;

	public ClaudeEventSummaryUpdater(
		string apiKey,
		string model,
		string prompt,
		ILogger<ClaudeEventSummaryUpdater> logger)
	{
		_apiKey = apiKey;
		_model = model;
		_prompt = prompt;
		_logger = logger;
	}

	public async Task<EventSummaryUpdateResult> UpdateSummaryAsync(
		Event evt,
		List<string> newFacts,
		CancellationToken cancellationToken = default)
	{
		var client = new AnthropicClient(new APIAuthentication(_apiKey));

		var userPrompt = $"""
            CURRENT EVENT SUMMARY:
            {evt.Summary}

            NEW FACTS TO INCORPORATE:
            {string.Join("\n", newFacts.Select((f, i) => $"{i + 1}. {f}"))}
            """;

		var request = new MessageParameters
		{
			Model = _model,
			MaxTokens = 512,
			System = [new SystemMessage(_prompt)],
			Messages = [new Message(RoleType.User, userPrompt)]
		};

		var sw = Stopwatch.StartNew();
		_logger.LogDebug("Calling {Provider} {Model} with {PromptChars} chars",
			"Anthropic", _model, userPrompt.Length);

		var response = await client.Messages.GetClaudeMessageAsync(request, cancellationToken);

		sw.Stop();
		_logger.LogDebug("{Provider} {Model} succeeded in {DurationMs}ms",
			"Anthropic", _model, sw.ElapsedMilliseconds);

		var raw = response.Content.FirstOrDefault()?.ToString() ?? string.Empty;

		try
		{
			return ParseResult(raw);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "{Provider} {Model} returned unparseable response",
				"Anthropic", _model);
			throw;
		}
	}

	private static EventSummaryUpdateResult ParseResult(string json)
	{
		json = json
			.Replace("```json", string.Empty)
			.Replace("```", string.Empty)
			.Trim();

		var doc = JsonSerializer.Deserialize<JsonElement>(json);

		if (!doc.TryGetProperty("updated_summary", out var summaryElement))
			throw new InvalidOperationException("Claude returned no 'updated_summary' field");

		var summary = summaryElement.GetString();

		if (string.IsNullOrWhiteSpace(summary))
			throw new InvalidOperationException("Claude returned empty updated_summary");

		var intrinsicImportance = doc.TryGetProperty("intrinsic_importance", out var importanceElement)
			? importanceElement.GetString() ?? "medium"
			: "medium";

		return new EventSummaryUpdateResult(summary, intrinsicImportance);
	}
}
