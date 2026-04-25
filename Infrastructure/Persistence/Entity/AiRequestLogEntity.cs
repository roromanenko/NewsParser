namespace Infrastructure.Persistence.Entity;

public class AiRequestLogEntity
{
    public Guid Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Worker { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;

    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CacheCreationInputTokens { get; init; }
    public int CacheReadInputTokens { get; init; }
    public int TotalTokens { get; init; }

    public decimal CostUsd { get; init; }
    public int LatencyMs { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }

    public Guid CorrelationId { get; init; }
    public Guid? ArticleId { get; init; }
}
