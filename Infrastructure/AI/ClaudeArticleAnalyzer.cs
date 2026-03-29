using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Core.DomainModels;
using Core.Interfaces.AI;
using Core.DomainModels.AI;
using System.Text.Json;

namespace Infrastructure.AI;

public class ClaudeArticleAnalyzer : IArticleAnalyzer
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly string _prompt;

	public ClaudeArticleAnalyzer(string apiKey, string model, string prompt)
	{
		_apiKey = apiKey;
		_model = model;
		_prompt = prompt;
	}

	public async Task<ArticleAnalysisResult> AnalyzeAsync(RawArticle rawArticle, CancellationToken cancellationToken = default)
	{
		var client = new AnthropicClient(new APIAuthentication(_apiKey));

		var userPrompt = $"""
            SOURCE METADATA:
            Published: {rawArticle.PublishedAt:yyyy-MM-dd HH:mm UTC}
            Source URL: {rawArticle.OriginalUrl}
            Detected Language: {(string.IsNullOrWhiteSpace(rawArticle.Language) ? "unknown" : rawArticle.Language)}
            RSS Categories: {(rawArticle.Category.Count > 0 ? string.Join(", ", rawArticle.Category) : "none")}

            ARTICLE:
            Title: {rawArticle.Title}
            Content: {rawArticle.Content}
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

		var response = await client.Messages.GetClaudeMessageAsync(request, cancellationToken);
		var content = response.Content.FirstOrDefault()?.ToString() ?? string.Empty;
		return ParseAnalysisResult(content);
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