using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Core.DomainModels;
using Core.Interfaces.AI;
using System.Text.Json;

namespace Infrastructure.AI;

public class HaikuKeyFactsExtractor : IKeyFactsExtractor
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly string _systemPrompt;

	public HaikuKeyFactsExtractor(string apiKey, string model, string systemPrompt)
	{
		_apiKey = apiKey;
		_model = model;
		_systemPrompt = systemPrompt;
	}

	public async Task<List<string>> ExtractAsync(Article article, CancellationToken cancellationToken = default)
	{
		var client = new AnthropicClient(new APIAuthentication(_apiKey));

		var userPrompt = $"""
            TITLE: {article.Title}

            SUMMARY: {article.Summary ?? string.Empty}

            CONTENT: {article.OriginalContent ?? string.Empty}
            """;

		var request = new MessageParameters
		{
			Model = _model,
			MaxTokens = 512,
			System = [new SystemMessage(_systemPrompt)],
			Messages = [new Message(RoleType.User, userPrompt)]
		};

		try
		{
			var response = await client.Messages.GetClaudeMessageAsync(request, cancellationToken);
			var raw = response.Content.FirstOrDefault()?.ToString() ?? string.Empty;
			return ParseFacts(raw);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			return [];
		}
	}

	private static List<string> ParseFacts(string json)
	{
		try
		{
			json = json
				.Replace("```json", string.Empty)
				.Replace("```", string.Empty)
				.Trim();

			var doc = JsonSerializer.Deserialize<JsonElement>(json);

			if (!doc.TryGetProperty("facts", out var factsElement))
				return [];

			return factsElement.EnumerateArray()
				.Select(f => f.GetString() ?? string.Empty)
				.Where(f => !string.IsNullOrWhiteSpace(f))
				.ToList();
		}
		catch
		{
			return [];
		}
	}
}
