using Core.DomainModels;
using Infrastructure.Persistence.Entity;

namespace Infrastructure.Persistence.Mappers;

public static class AiRequestLogMapper
{
    public static AiRequestLog ToDomain(this AiRequestLogEntity entity) => new()
    {
        Id = entity.Id,
        Timestamp = entity.Timestamp,
        Worker = entity.Worker,
        Provider = entity.Provider,
        Operation = entity.Operation,
        Model = entity.Model,
        InputTokens = entity.InputTokens,
        OutputTokens = entity.OutputTokens,
        CacheCreationInputTokens = entity.CacheCreationInputTokens,
        CacheReadInputTokens = entity.CacheReadInputTokens,
        TotalTokens = entity.TotalTokens,
        CostUsd = entity.CostUsd,
        LatencyMs = entity.LatencyMs,
        Status = Enum.Parse<AiRequestStatus>(entity.Status),
        ErrorMessage = entity.ErrorMessage,
        CorrelationId = entity.CorrelationId,
        ArticleId = entity.ArticleId
    };

    public static AiRequestLogEntity ToEntity(this AiRequestLog domain) => new()
    {
        Id = domain.Id,
        Timestamp = domain.Timestamp,
        Worker = domain.Worker,
        Provider = domain.Provider,
        Operation = domain.Operation,
        Model = domain.Model,
        InputTokens = domain.InputTokens,
        OutputTokens = domain.OutputTokens,
        CacheCreationInputTokens = domain.CacheCreationInputTokens,
        CacheReadInputTokens = domain.CacheReadInputTokens,
        TotalTokens = domain.TotalTokens,
        CostUsd = domain.CostUsd,
        LatencyMs = domain.LatencyMs,
        Status = domain.Status.ToString(),
        ErrorMessage = domain.ErrorMessage,
        CorrelationId = domain.CorrelationId,
        ArticleId = domain.ArticleId
    };
}
