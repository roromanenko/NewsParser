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

internal class ClaudeEventClassifier : IEventClassifier
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly string _prompt;
	private readonly IAiRequestLogger _aiRequestLogger;
	private readonly ILogger<ClaudeEventClassifier> _logger;

	public ClaudeEventClassifier(
		string apiKey,
		string model,
		string prompt,
		ILogger<ClaudeEventClassifier> logger,
		IAiRequestLogger aiRequestLogger)
	{
		_apiKey = apiKey;
		_model = model;
		_prompt = prompt;
		_logger = logger;
		_aiRequestLogger = aiRequestLogger;
	}

	public async Task<EventClassificationResult> ClassifyAsync(
		Article article,
		List<Event> candidateEvents,
		CancellationToken cancellationToken = default)
	{
		var client = new AnthropicClient(new APIAuthentication(_apiKey));

		var candidatesText = candidateEvents.Count == 0
			? "No candidate events found."
			: string.Join("\n\n", candidateEvents.Select((e, i) => BuildCandidateBlock(e, i)));

		var userPrompt = $"""
            ARTICLE TO CLASSIFY:
            Id: {article.Id}
            Title: {article.Title}
            Summary: {article.Summary}
            Category: {article.Category}
            Tags: {string.Join(", ", article.Tags)}
            Language: {article.Language}

            CANDIDATE EVENTS:
            {candidatesText}
            """;

		var messages = new List<Message>
		{
			new Message(RoleType.User, userPrompt)
		};

		var request = new MessageParameters
		{
			Model = _model,
			MaxTokens = 1024,
			System = [new SystemMessage(_prompt)],
			Messages = messages
		};

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
			Operation: nameof(ClassifyAsync),
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

		var content = response!.Content.FirstOrDefault()?.ToString() ?? string.Empty;

		try
		{
			return ParseResult(content);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "{Provider} {Model} returned unparseable response",
				"Anthropic", _model);
			throw;
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

	private static string BuildCandidateBlock(Event e, int index)
	{
		var knownFacts = e.EventUpdates.Count == 0
			? "  (none)"
			: string.Join("\n", e.EventUpdates.Select((u, i) => $"  [{i + 1}] {u.FactSummary}"));

		var articles = e.Articles.Count == 0
			? "  (none)"
			: string.Join("\n", e.Articles.Select(a =>
				$"  - {a.Title} | Key facts: {string.Join("; ", a.KeyFacts)}"));

		return $"""
            CANDIDATE EVENT [{index + 1}]:
            Id: {e.Id}
            Title: {e.Title}
            Summary: {e.Summary}
            Last Updated: {e.LastUpdatedAt:yyyy-MM-dd HH:mm UTC}
            Known Facts:
            {knownFacts}
            Articles in this event:
            {articles}
            """;
	}

	private static EventClassificationResult ParseResult(string json)
	{
		json = json
			.Replace("```json", string.Empty)
			.Replace("```", string.Empty)
			.Trim();

		var result = JsonSerializer.Deserialize<EventClassificationResult>(json, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower

		}) ?? throw new InvalidOperationException("Claude returned null classification result");

		return result;
	}
}
