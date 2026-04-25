namespace Api.Models;

public record AiOperationsMetricsDto(
    decimal TotalCostUsd,
    int TotalCalls,
    int SuccessCalls,
    int ErrorCalls,
    double AverageLatencyMs,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalCacheCreationInputTokens,
    int TotalCacheReadInputTokens,
    List<AiMetricsTimeBucketDto> TimeSeries,
    List<AiMetricsBreakdownRowDto> ByModel,
    List<AiMetricsBreakdownRowDto> ByWorker,
    List<AiMetricsBreakdownRowDto> ByProvider);

public record AiMetricsTimeBucketDto(
    DateTimeOffset Bucket,
    string Provider,
    decimal CostUsd,
    int Calls,
    int Tokens);

public record AiMetricsBreakdownRowDto(
    string Key,
    int Calls,
    decimal CostUsd,
    int Tokens);

public record AiRequestLogDto(
    Guid Id,
    DateTimeOffset Timestamp,
    string Worker,
    string Provider,
    string Operation,
    string Model,
    int InputTokens,
    int OutputTokens,
    int CacheCreationInputTokens,
    int CacheReadInputTokens,
    int TotalTokens,
    decimal CostUsd,
    int LatencyMs,
    string Status,
    string? ErrorMessage,
    Guid CorrelationId,
    Guid? ArticleId);

public record AiOperationsMetricsQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Provider,
    string? Worker,
    string? Model);

public record AiRequestsListQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Provider,
    string? Worker,
    string? Model,
    string? Status,
    string? Search,
    int Page = 1,
    int PageSize = 20);
