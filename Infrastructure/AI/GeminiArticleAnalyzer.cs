using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Infrastructure.AI;

public class GeminiArticleAnalyzer : IArticleAnalyzer
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly string _prompt;
	private readonly HttpClient _httpClient;
	private readonly ILogger<GeminiArticleAnalyzer> _logger;

	public GeminiArticleAnalyzer(
		string apiKey,
		string model,
		string prompt,
		HttpClient httpClient,
		ILogger<GeminiArticleAnalyzer> logger)
	{
		_apiKey = apiKey;
		_model = model;
		_prompt = prompt;
		_httpClient = httpClient;
		_logger = logger;
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
		var text = doc.RootElement
			.GetProperty("candidates")[0]
			.GetProperty("content")
			.GetProperty("parts")[0]
			.GetProperty("text")
			.GetString() ?? string.Empty;

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
}
