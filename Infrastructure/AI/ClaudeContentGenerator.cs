using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Core.DomainModels;
using Core.Interfaces.AI;
using System.Text.Json;

namespace Infrastructure.AI;

public class ClaudeContentGenerator : IContentGenerator
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly Dictionary<Platform, string> _basePrompts;

	public ClaudeContentGenerator(
		string apiKey,
		string model,
		Dictionary<Platform, string> basePrompts)
	{
		_apiKey = apiKey;
		_model = model;
		_basePrompts = basePrompts;
	}

	public async Task<string> GenerateForPlatformAsync(
	Article article,
	PublishTarget target,
	CancellationToken cancellationToken = default,
	string? updateContext = null)
	{
		if (!_basePrompts.TryGetValue(target.Platform, out var basePrompt))
			throw new InvalidOperationException(
				$"No base prompt configured for platform {target.Platform}");

		var systemPrompt = string.IsNullOrWhiteSpace(target.SystemPrompt)
			? basePrompt
			: $"{basePrompt}\n\nCHANNEL STYLE INSTRUCTIONS:\n{target.SystemPrompt}";

		var client = new AnthropicClient(new APIAuthentication(_apiKey));

		var userPrompt = updateContext is not null
			? $"""
            CHANNEL: {target.Name}
            THIS IS AN UPDATE to an existing published story.
            FORMAT: Short reply message, 1-3 sentences max.
            NEW FACT TO PUBLISH:
            {updateContext}
            ORIGINAL ARTICLE CONTEXT:
            Title: {article.Title}
            Category: {article.Category}
            Tags: {string.Join(", ", article.Tags)}
            """
			: $"""
            CHANNEL: {target.Name}
            ARTICLE METADATA:
            Category: {article.Category}
            Tags: {string.Join(", ", article.Tags)}
            Sentiment: {article.Sentiment}
            Source URL: {article.OriginalUrl}
            ARTICLE:
            Title: {article.Title}
            Content: {article.Content}
            """;

		var request = new MessageParameters
		{
			Model = _model,
			MaxTokens = 1024,
			System = [new SystemMessage(systemPrompt)],
			Messages = [new Message(RoleType.User, userPrompt)]
		};

		var response = await client.Messages.GetClaudeMessageAsync(request, cancellationToken);
		var raw = response.Content.FirstOrDefault()?.ToString() ?? string.Empty;
		return ParseContent(raw, target.Platform);
	}

	private static string ParseContent(string json, Platform platform)
	{
		json = json
			.Replace("```json", string.Empty)
			.Replace("```", string.Empty)
			.Trim();

		var doc = JsonSerializer.Deserialize<JsonElement>(json);

		if (!doc.TryGetProperty("content", out var contentElement))
			throw new InvalidOperationException(
				$"Claude returned no 'content' field for platform {platform}");

		var content = contentElement.GetString();

		if (string.IsNullOrWhiteSpace(content))
			throw new InvalidOperationException(
				$"Claude returned empty content for platform {platform}");

		return content;
	}
}