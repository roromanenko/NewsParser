namespace Core.DomainModels.AI;

public record AiUsage(
    int InputTokens,
    int OutputTokens,
    int CacheCreationInputTokens,
    int CacheReadInputTokens);
