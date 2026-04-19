using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Core.DomainModels;
using Core.Interfaces.AI;
using Core.DomainModels.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Infrastructure.AI;

public class ClaudeArticleAnalyzer : IArticleAnalyzer
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly string _prompt;
	private readonly ILogger<ClaudeArticleAnalyzer> _logger;

	public ClaudeArticleAnalyzer(
		string apiKey,
		string model,
		string prompt,
		ILogger<ClaudeArticleAnalyzer> logger)
	{
		_apiKey = apiKey;
		_model = model;
		_prompt = prompt;
		_logger = logger;
	}

	public async Task<ArticleAnalysisResult> AnalyzeAsync(Article article, CancellationToken cancellationToken = default)
	{
		var client = new AnthropicClient(new APIAuthentication(_apiKey));

		var userPrompt = $"""
            SOURCE METADATA:
            Published: {article.PublishedAt:yyyy-MM-dd HH:mm UTC}
            Source URL: {article.OriginalUrl}
            Detected Language: {(string.IsNullOrWhiteSpace(article.Language) ? "unknown" : article.Language)}

            ARTICLE:
            Title: {article.Title}
            Content: {article.OriginalContent}
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

		var response = await client.Messages.GetClaudeMessageAsync(request, cancellationToken);

		sw.Stop();
		_logger.LogDebug("{Provider} {Model} succeeded in {DurationMs}ms",
			"Anthropic", _model, sw.ElapsedMilliseconds);

		var content = response.Content.FirstOrDefault()?.ToString() ?? string.Empty;

		try
		{
			return ParseAnalysisResult(content);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "{Provider} {Model} returned unparseable response",
				"Anthropic", _model);
			throw;
		}
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
		}) ?? throw new InvalidOperationException("Claude returned null result");

		if (string.IsNullOrWhiteSpace(result.Category))
			throw new InvalidOperationException("Claude analyzer returned empty Category");

		if (string.IsNullOrWhiteSpace(result.Language))
			throw new InvalidOperationException("Claude analyzer returned empty Language");

		if (string.IsNullOrWhiteSpace(result.Sentiment))
			throw new InvalidOperationException("Claude analyzer returned empty Sentiment");

		if (result.Tags.Count == 0)
			throw new InvalidOperationException("Claude analyzer returned empty Tags");

		return result;
	}
}
