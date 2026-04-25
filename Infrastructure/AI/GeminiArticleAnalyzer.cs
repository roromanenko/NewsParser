using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using Infrastructure.AI.Telemetry;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Infrastructure.AI;

internal class GeminiArticleAnalyzer : IArticleAnalyzer
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly string _prompt;
	private readonly HttpClient _httpClient;
	private readonly IAiRequestLogger _aiRequestLogger;
	private readonly ILogger<GeminiArticleAnalyzer> _logger;

	public GeminiArticleAnalyzer(
		string apiKey,
		string model,
		string prompt,
		HttpClient httpClient,
		ILogger<GeminiArticleAnalyzer> logger,
		IAiRequestLogger aiRequestLogger)
	{
		_apiKey = apiKey;
		_model = model;
		_prompt = prompt;
		_httpClient = httpClient;
		_logger = logger;
		_aiRequestLogger = aiRequestLogger;
	}

	public async Task<ArticleAnalysisResult> AnalyzeAsync(Article article, CancellationToken cancellationToken = default)
	{
		var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

		var userPrompt = $"""
        {_prompt}

        SOURCE METADATA:
        Published: {article.PublishedAt:yyyy-MM-dd HH:mm UTC}
        Source URL: {article.OriginalUrl}
        Detected Language: {(string.IsNullOrWhiteSpace(article.Language) ? "unknown" : article.Language)}

        ARTICLE:
        Title: {article.Title}
        Content: {article.OriginalContent}
        """;

		var requestBody = JsonSerializer.Serialize(new
		{
			contents = new[]
			{
				new
				{
					parts = new[] { new { text = userPrompt } }
				}
			},
			generationConfig = new { responseMimeType = "application/json" }
		});

		var sw = Stopwatch.StartNew();
		_logger.LogDebug("Calling {Provider} {Model} with {PromptChars} chars",
			"Gemini", _model, userPrompt.Length);

		HttpResponseMessage? httpResponse = null;
		Exception? failure = null;
		try
		{
			httpResponse = await _httpClient.PostAsync(
				url,
				new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json"),
				cancellationToken);
			httpResponse.EnsureSuccessStatusCode();
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex) { failure = ex; }
		sw.Stop();

		if (failure is null)
			_logger.LogDebug("{Provider} {Model} succeeded in {DurationMs}ms",
				"Gemini", _model, sw.ElapsedMilliseconds);

		AiUsage usage;
		string responseJson = string.Empty;
		JsonDocument? doc = null;

		if (failure is null)
		{
			responseJson = await httpResponse!.Content.ReadAsStringAsync(cancellationToken);
			doc = JsonDocument.Parse(responseJson);
			usage = ParseGeminiUsage(doc);
		}
		else
		{
			usage = new AiUsage(0, 0, 0, 0);
		}

		await _aiRequestLogger.LogAsync(new AiRequestLogEntry(
			Provider: "Gemini",
			Operation: nameof(AnalyzeAsync),
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

		var text = doc!.RootElement
			.GetProperty("candidates")[0]
			.GetProperty("content")
			.GetProperty("parts")[0]
			.GetProperty("text")
			.GetString() ?? string.Empty;

		doc.Dispose();

		try
		{
			return ArticleJsonHelper.ParseAnalysisResult(text);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "{Provider} {Model} returned unparseable response",
				"Gemini", _model);
			throw;
		}
	}

	private static AiUsage ParseGeminiUsage(JsonDocument doc)
	{
		if (!doc.RootElement.TryGetProperty("usageMetadata", out var meta))
			return new AiUsage(0, 0, 0, 0);

		var inputTokens = meta.TryGetProperty("promptTokenCount", out var input) ? input.GetInt32() : 0;
		var outputTokens = meta.TryGetProperty("candidatesTokenCount", out var output) ? output.GetInt32() : 0;

		return new AiUsage(inputTokens, outputTokens, 0, 0);
	}
}
