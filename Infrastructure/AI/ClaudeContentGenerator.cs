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
		Event evt,
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
			? BuildUpdatePrompt(evt, target, updateContext)
			: BuildEventPrompt(evt, target);

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

	private static string BuildEventPrompt(Event evt, PublishTarget target)
	{
		var articlesSection = BuildArticlesSection(evt.Articles);

		return $"""
            CHANNEL: {target.Name}
            EVENT TITLE: {evt.Title}
            EVENT SUMMARY: {evt.Summary}
            SOURCES:
            {articlesSection}
            """;
	}

	private static string BuildUpdatePrompt(Event evt, PublishTarget target, string updateContext)
	{
		var initiator = evt.Articles.FirstOrDefault(a => a.Role == ArticleRole.Initiator)
			?? evt.Articles.FirstOrDefault()
			?? new Article();

		var combinedTags = evt.Articles
			.SelectMany(a => a.Tags)
			.Distinct()
			.ToList();

		return $"""
            CHANNEL: {target.Name}
            THIS IS AN UPDATE to an existing published story.
            FORMAT: Short reply message, 1-3 sentences max.
            NEW FACT TO PUBLISH:
            {updateContext}
            ORIGINAL EVENT CONTEXT:
            Title: {evt.Title}
            Category: {initiator.Category}
            Tags: {string.Join(", ", combinedTags)}
            """;
	}

	private static string BuildArticlesSection(List<Article> articles)
	{
		if (articles.Count == 0)
			return "(no articles)";

		var parts = articles.Select((article, index) =>
		{
			var keyFactsText = article.KeyFacts.Count > 0
				? string.Join("\n", article.KeyFacts.Select(f => $"  - {f}"))
				: "  (none)";

			return $"""
                [{index + 1}]
                Summary: {article.Summary ?? "(no summary)"}
                Key Facts:
                {keyFactsText}
                """;
		});

		return string.Join("\n\n", parts);
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
