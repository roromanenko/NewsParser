using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using Infrastructure.AI.Telemetry;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Infrastructure.AI;

internal class HaikuKeyFactsExtractor : IKeyFactsExtractor
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly string _systemPrompt;
	private readonly IAiRequestLogger _aiRequestLogger;
	private readonly ILogger<HaikuKeyFactsExtractor> _logger;

	public HaikuKeyFactsExtractor(
		string apiKey,
		string model,
		string systemPrompt,
		ILogger<HaikuKeyFactsExtractor> logger,
		IAiRequestLogger aiRequestLogger)
	{
		_apiKey = apiKey;
		_model = model;
		_systemPrompt = systemPrompt;
		_logger = logger;
		_aiRequestLogger = aiRequestLogger;
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

			MessageResponse? response = null;
			Exception? failure = null;
			try
			{
				response = await client.Messages.GetClaudeMessageAsync(request, cancellationToken);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex) { failure = ex; }
			sw.Stop();

			if (failure is null)
				_logger.LogDebug("{Provider} {Model} succeeded in {DurationMs}ms",
					"Anthropic", _model, sw.ElapsedMilliseconds);

			var usage = BuildUsage(response);
			await _aiRequestLogger.LogAsync(new AiRequestLogEntry(
				Provider: "Anthropic",
				Operation: nameof(ExtractAsync),
				Model: _model,
				Usage: usage,
				LatencyMs: (int)sw.ElapsedMilliseconds,
				Status: failure is null ? AiRequestStatus.Success : AiRequestStatus.Error,
				ErrorMessage: failure?.Message,
				CorrelationId: AiCallContext.CurrentCorrelationId,
				ArticleId: AiCallContext.CurrentArticleId,
				Worker: AiCallContext.CurrentWorker),
				cancellationToken);

			if (failure is not null) throw failure;

			var raw = response!.Content.FirstOrDefault()?.ToString() ?? string.Empty;
			return ParseFacts(raw);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "{Provider} {Model} returned unparseable response",
				"Anthropic", _model);
			return [];
		}
	}

	private static AiUsage BuildUsage(MessageResponse? response) =>
		response is null
			? new AiUsage(0, 0, 0, 0)
			: new AiUsage(
				response.Usage.InputTokens,
				response.Usage.OutputTokens,
				response.Usage.CacheCreationInputTokens,
				response.Usage.CacheReadInputTokens);

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
