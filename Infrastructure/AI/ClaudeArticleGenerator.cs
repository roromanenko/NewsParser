using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Core.DomainModels;
using Core.Interfaces.AI;
using Core.DomainModels.AI;

namespace Infrastructure.AI;

public class ClaudeArticleGenerator : IArticleGenerator
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly string _prompt;
	private readonly string _outputLanguage;

	public ClaudeArticleGenerator(string apiKey, string model, string prompt, string outputLanguage)
	{
		_apiKey = apiKey;
		_model = model;
		_prompt = prompt;
		_outputLanguage = outputLanguage;
	}

	public async Task<ArticleGenerationResult> GenerateAsync(
	RawArticle rawArticle,
	ArticleAnalysisResult analysis,
	CancellationToken cancellationToken = default)
	{
		var client = new AnthropicClient(new APIAuthentication(_apiKey));

		var userPrompt = $"""
        OUTPUT LANGUAGE: {_outputLanguage}

        SOURCE METADATA:
        Published: {rawArticle.PublishedAt:yyyy-MM-dd HH:mm UTC}
        Source URL: {rawArticle.OriginalUrl}

        ANALYSIS RESULTS:
        Language: {analysis.Language}
        Category: {analysis.Category}
        Sentiment: {analysis.Sentiment}
        Tags: {string.Join(", ", analysis.Tags)}
        Summary: {analysis.Summary}

        ORIGINAL ARTICLE:
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
			MaxTokens = 2048,
			System = [new SystemMessage(_prompt)],
			Messages = messages
		};

		var response = await client.Messages.GetClaudeMessageAsync(request, cancellationToken);
		var content = response.Content.FirstOrDefault()?.ToString() ?? string.Empty;
		return ParseGenerationResult(content);
	}

	private static ArticleGenerationResult ParseGenerationResult(string content)
	{
		content = content
			.Replace("```json", string.Empty)
			.Replace("```", string.Empty)
			.Trim();

		var result = System.Text.Json.JsonSerializer.Deserialize<ArticleGenerationResult>(content, new System.Text.Json.JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		}) ?? throw new InvalidOperationException("Claude returned null result");

		if (string.IsNullOrWhiteSpace(result.Title))
			throw new InvalidOperationException("Claude returned empty Title");

		if (string.IsNullOrWhiteSpace(result.Content))
			throw new InvalidOperationException("Claude returned empty Content");

		return result;
	}
}