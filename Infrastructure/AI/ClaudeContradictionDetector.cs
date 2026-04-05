using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using System.Text.Json;

namespace Infrastructure.AI;

public class ClaudeContradictionDetector : IContradictionDetector
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly string _prompt;

    public ClaudeContradictionDetector(string apiKey, string model, string prompt)
    {
        _client = new AnthropicClient(new APIAuthentication(apiKey));
        _model = model;
        _prompt = prompt;
    }

    public async Task<List<ContradictionInput>> DetectAsync(
        Article article,
        Event targetEvent,
        CancellationToken cancellationToken = default)
    {

        var userPrompt = BuildUserPrompt(article, targetEvent);

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

        var response = await _client.Messages.GetClaudeMessageAsync(request, cancellationToken);
        var content = response.Content.FirstOrDefault()?.ToString() ?? string.Empty;

        return ParseResult(content);
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
