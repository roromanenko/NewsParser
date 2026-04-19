using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Infrastructure.AI;

public class ClaudeContradictionDetector : IContradictionDetector
{
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, };

	private readonly AnthropicClient _client;
	private readonly string _model;
	private readonly string _prompt;
	private readonly ILogger<ClaudeContradictionDetector> _logger;

	public ClaudeContradictionDetector(
		string apiKey,
		string model,
		string prompt,
		ILogger<ClaudeContradictionDetector> logger)
	{
		_client = new AnthropicClient(new APIAuthentication(apiKey));
		_model = model;
		_prompt = prompt;
		_logger = logger;
	}

	public async Task<List<ContradictionInput>> DetectAsync(
		Article article,
		Event targetEvent,
		CancellationToken cancellationToken = default)
	{

		var userPrompt = BuildUserPrompt(article, targetEvent);
		var toolName = "report_contradictions";
		var tools = new List<Anthropic.SDK.Common.Tool>
		{
			new Function(
				name: toolName,
				description: "Reports all detected factual contradictions between the new article and the target event. Always call this tool exactly once with the complete list of contradictions found (or an empty list if none).",
				parameters: JsonNode.Parse("""
				{
					"type": "object",
					"properties": {
						"contradictions": {
							"type": "array",
							"description": "List of contradictions. Empty array if no contradictions found.",
							"items": {
								"type": "object",
								"properties": {
									"article_ids": {
										"type": "array",
										"items": { "type": "string" },
										"description": "GUIDs of the new article and any contradicted event articles."
									},
									"description": {
										"type": "string",
										"description": "Concise description of the contradiction in Ukrainian."
									}
								},
								"required": ["article_ids", "description"]
							}
						}
					},
					"required": ["contradictions"]
				}
				""")
			)
		};

		var request = new MessageParameters
		{
			Model = _model,
			MaxTokens = 1024,
			System = [new SystemMessage(_prompt)],
			Messages = new List<Message>
		{
			new Message(RoleType.User, userPrompt)
		},
			Tools = tools,
			ToolChoice = new ToolChoice
			{
				Type = ToolChoiceType.Tool,
				Name = toolName
			}
		};

		var sw = Stopwatch.StartNew();
		_logger.LogDebug("Calling {Provider} {Model} with {PromptChars} chars",
			"Anthropic", _model, userPrompt.Length);

		var response = await _client.Messages.GetClaudeMessageAsync(request, cancellationToken);

		sw.Stop();
		_logger.LogDebug("{Provider} {Model} succeeded in {DurationMs}ms",
			"Anthropic", _model, sw.ElapsedMilliseconds);

		// Extract the tool_use block — guaranteed to exist because ToolChoice forces it
		var toolUse = response.Content.OfType<ToolUseContent>().FirstOrDefault();
		if (toolUse is null)
		{
			return new List<ContradictionInput>();
		}

		// toolUse.Input is already a parsed JSON object matching our schema
		var json = toolUse.Input.ToString();
		using var doc = JsonDocument.Parse(json);
		var contradictionsJson = doc.RootElement.GetProperty("contradictions").GetRawText();

		try
		{
			return ParseResult(contradictionsJson);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "{Provider} {Model} returned unparseable response",
				"Anthropic", _model);
			throw;
		}
	}

	private static string BuildUserPrompt(Article article, Event targetEvent)
	{
		var knownFacts = targetEvent.EventUpdates.Count == 0
			? "No known facts recorded."
			: string.Join("\n", targetEvent.EventUpdates.Select((u, i) => $"  [{i + 1}] {u.FactSummary}"));

		var articlesList = targetEvent.Articles.Count == 0
			? "No articles recorded."
			: string.Join("\n", targetEvent.Articles.Select(a =>
				$"  - [{a.Id}] {a.Title} | Key facts: {string.Join("; ", a.KeyFacts)}"));

		return $"""
            NEW ARTICLE:
            Id: {article.Id}
            Title: {article.Title}
            Summary: {article.Summary}
            Key Facts: {string.Join("; ", article.KeyFacts)}

            TARGET EVENT:
            Id: {targetEvent.Id}
            Title: {targetEvent.Title}
            Summary: {targetEvent.Summary}
            Known Facts ({targetEvent.EventUpdates.Count}):
            {knownFacts}
            Articles in this event ({targetEvent.Articles.Count}):
            {articlesList}
            """;
	}

	private static List<ContradictionInput> ParseResult(string json)
	{
		// Strip markdown fences defensively — some model versions emit them despite the system prompt
		// instructing plain JSON output; this guard prevents a parse failure on those responses.
		json = json
			.Replace("```json", string.Empty)
			.Replace("```", string.Empty)
			.Trim();

		return JsonSerializer.Deserialize<List<ContradictionInput>>(json, JsonOptions) ?? [];
	}
}
