using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using System.Text.Json;

namespace Infrastructure.AI;

public class GeminiArticleAnalyzer : IArticleAnalyzer
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly string _prompt;
	private readonly HttpClient _httpClient;

	public GeminiArticleAnalyzer(string apiKey, string model, string prompt, HttpClient httpClient)
	{
		_apiKey = apiKey;
		_model = model;
		_prompt = prompt;
		_httpClient = httpClient;
	}

	public async Task<ArticleAnalysisResult> AnalyzeAsync(RawArticle rawArticle, CancellationToken cancellationToken = default)
	{
		var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

		var prompt = $"""
        {_prompt}

        SOURCE METADATA:
        Published: {rawArticle.PublishedAt:yyyy-MM-dd HH:mm UTC}
        Source URL: {rawArticle.OriginalUrl}
        Detected Language: {(string.IsNullOrWhiteSpace(rawArticle.Language) ? "unknown" : rawArticle.Language)}
        RSS Categories: {(rawArticle.Category.Count > 0 ? string.Join(", ", rawArticle.Category) : "none")}

        ARTICLE:
        Title: {rawArticle.Title}
        Content: {rawArticle.Content}
        """;

		var requestBody = JsonSerializer.Serialize(new
		{
			contents = new[]
			{
				new
				{
					parts = new[] { new { text = prompt } }
				}
			}
		});

		var httpResponse = await _httpClient.PostAsync(
			url,
			new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json"),
			cancellationToken);

		httpResponse.EnsureSuccessStatusCode();

		var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

		using var doc = JsonDocument.Parse(responseJson);
		var text = doc.RootElement
			.GetProperty("candidates")[0]
			.GetProperty("content")
			.GetProperty("parts")[0]
			.GetProperty("text")
			.GetString() ?? string.Empty;

		return ParseAnalysisResult(text);
	}

	private static ArticleAnalysisResult ParseAnalysisResult(string json)
	{
		json = json
			.Replace("```json", string.Empty)
			.Replace("```", string.Empty)
			.Trim();

		var result = JsonSerializer.Deserialize<ArticleAnalysisResult>(json, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		}) ?? throw new InvalidOperationException("Gemini returned null result");

		if (string.IsNullOrWhiteSpace(result.Category))
			throw new InvalidOperationException("Gemini returned empty Category");
		if (string.IsNullOrWhiteSpace(result.Language))
			throw new InvalidOperationException("Gemini returned empty Language");
		if (string.IsNullOrWhiteSpace(result.Sentiment))
			throw new InvalidOperationException("Gemini returned empty Sentiment");
		if (result.Tags.Count == 0)
			throw new InvalidOperationException("Gemini returned empty Tags");

		return result;
	}
}