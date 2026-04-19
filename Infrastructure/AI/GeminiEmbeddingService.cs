using Core.Interfaces.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Infrastructure.AI;

public class GeminiEmbeddingService : IGeminiEmbeddingService
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly HttpClient _httpClient;
	private readonly ILogger<GeminiEmbeddingService> _logger;

	public GeminiEmbeddingService(
		string apiKey,
		string model,
		HttpClient httpClient,
		ILogger<GeminiEmbeddingService> logger)
	{
		_apiKey = apiKey;
		_model = model;
		_httpClient = httpClient;
		_logger = logger;
	}

	public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
	{
		var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:embedContent?key={_apiKey}";
		var sanitizedUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:embedContent";

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

		var httpResponse = await _httpClient.PostAsync(
			url,
			new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json"),
			cancellationToken);

		httpResponse.EnsureSuccessStatusCode();

		sw.Stop();
		_logger.LogDebug("{Provider} {Model} succeeded in {DurationMs}ms",
			"Gemini", _model, sw.ElapsedMilliseconds);

		var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

		using var doc = JsonDocument.Parse(responseJson);
		var values = doc.RootElement
			.GetProperty("embedding")
			.GetProperty("values")
			.EnumerateArray()
			.Select(v => v.GetSingle())
			.ToArray();

		return values;
	}
}
