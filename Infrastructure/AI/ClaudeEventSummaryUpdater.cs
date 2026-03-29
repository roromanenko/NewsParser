using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Core.DomainModels;
using Core.Interfaces.AI;
using System.Text.Json;

namespace Infrastructure.AI;

public class ClaudeEventSummaryUpdater : IEventSummaryUpdater
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly string _prompt;

	public ClaudeEventSummaryUpdater(string apiKey, string model, string prompt)
	{
		_apiKey = apiKey;
		_model = model;
		_prompt = prompt;
	}

	public async Task<string> UpdateSummaryAsync(
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

		var response = await client.Messages.GetClaudeMessageAsync(request, cancellationToken);
		var raw = response.Content.FirstOrDefault()?.ToString() ?? string.Empty;

		return ParseResult(raw);
	}

	private static string ParseResult(string json)
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

		return summary;
	}
}