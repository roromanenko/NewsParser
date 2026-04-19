using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Core.DomainModels;
using Core.Interfaces.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Infrastructure.AI;

public class HaikuKeyFactsExtractor : IKeyFactsExtractor
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly string _systemPrompt;
	private readonly ILogger<HaikuKeyFactsExtractor> _logger;

	public HaikuKeyFactsExtractor(
		string apiKey,
		string model,
		string systemPrompt,
		ILogger<HaikuKeyFactsExtractor> logger)
	{
		_apiKey = apiKey;
		_model = model;
		_systemPrompt = systemPrompt;
		_logger = logger;
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
			var sw = Stopwatch.StartNew();
			_logger.LogDebug("Calling {Provider} {Model} with {PromptChars} chars",
				"Anthropic", _model, userPrompt.Length);

			var response = await client.Messages.GetClaudeMessageAsync(request, cancellationToken);

			sw.Stop();
			_logger.LogDebug("{Provider} {Model} succeeded in {DurationMs}ms",
				"Anthropic", _model, sw.ElapsedMilliseconds);

			var raw = response.Content.FirstOrDefault()?.ToString() ?? string.Empty;
			return ParseFacts(raw);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "{Provider} {Model} returned unparseable response",
				"Anthropic", _model);
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
