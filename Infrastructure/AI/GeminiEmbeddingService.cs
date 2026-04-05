using Core.Interfaces.AI;
using System.Text.Json;

namespace Infrastructure.AI;

public class GeminiEmbeddingService : IGeminiEmbeddingService
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly HttpClient _httpClient;

	public GeminiEmbeddingService(string apiKey, string model, HttpClient httpClient)
	{
		_apiKey = apiKey;
		_model = model;
		_httpClient = httpClient;
	}

	public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
	{
		var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:embedContent?key={_apiKey}";
		Console.WriteLine($"[DEBUG] Embedding URL: {url}");

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

		var httpResponse = await _httpClient.PostAsync(
			url,
			new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json"),
			cancellationToken);

		httpResponse.EnsureSuccessStatusCode();

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