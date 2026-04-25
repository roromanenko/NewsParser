using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using Infrastructure.AI.Telemetry;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Infrastructure.AI;

internal class GeminiEmbeddingService : IGeminiEmbeddingService
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly HttpClient _httpClient;
	private readonly IAiRequestLogger _aiRequestLogger;
	private readonly ILogger<GeminiEmbeddingService> _logger;

	public GeminiEmbeddingService(
		string apiKey,
		string model,
		HttpClient httpClient,
		ILogger<GeminiEmbeddingService> logger,
		IAiRequestLogger aiRequestLogger)
	{
		_apiKey = apiKey;
		_model = model;
		_httpClient = httpClient;
		_logger = logger;
		_aiRequestLogger = aiRequestLogger;
	}

	public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
	{
		var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:embedContent?key={_apiKey}";

		_logger.LogDebug("Calling {Provider} {Model} with {PromptChars} chars",
			"Gemini", _model, text.Length);

		var requestBody = JsonSerializer.Serialize(new
		{
			model = $"models/{_model}",
			content = new
			{
				parts = new[] { new { text } }
			},
			taskType = "SEMANTIC_SIMILARITY",
			outputDimensionality = 768
		});

		var sw = Stopwatch.StartNew();

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
			Operation: nameof(GenerateEmbeddingAsync),
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

		var values = doc!.RootElement
			.GetProperty("embedding")
			.GetProperty("values")
			.EnumerateArray()
			.Select(v => v.GetSingle())
			.ToArray();

		doc.Dispose();

		return values;
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
