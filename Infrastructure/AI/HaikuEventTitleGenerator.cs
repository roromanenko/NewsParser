using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using Infrastructure.AI.Telemetry;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Infrastructure.AI;

internal class HaikuEventTitleGenerator : IEventTitleGenerator
{
	private readonly AnthropicClient _client;
	private readonly string _model;
	private readonly string _systemPrompt;
	private readonly IAiRequestLogger _aiRequestLogger;
	private readonly ILogger<HaikuEventTitleGenerator> _logger;

	public HaikuEventTitleGenerator(
		string apiKey,
		string model,
		string systemPrompt,
		ILogger<HaikuEventTitleGenerator> logger,
		IAiRequestLogger aiRequestLogger)
	{
		_client = new AnthropicClient(new APIAuthentication(apiKey));
		_model = model;
		_systemPrompt = systemPrompt;
		_logger = logger;
		_aiRequestLogger = aiRequestLogger;
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
			var sw = Stopwatch.StartNew();
			_logger.LogDebug("Calling {Provider} {Model} with {PromptChars} chars",
				"Anthropic", _model, userMessage.Length);

			MessageResponse? response = null;
			Exception? failure = null;
			try
			{
				response = await _client.Messages.GetClaudeMessageAsync(request, cancellationToken);
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
				Operation: nameof(GenerateTitleAsync),
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
			return raw.Trim();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Failed to generate event title via AI");
			return string.Empty;
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
