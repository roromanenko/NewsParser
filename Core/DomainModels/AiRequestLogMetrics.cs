namespace Core.DomainModels;

public record AiMetricsTotals(
    decimal TotalCostUsd,
    int TotalCalls,
    int SuccessCalls,
    int ErrorCalls,
    double AverageLatencyMs,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalCacheCreationInputTokens,
    int TotalCacheReadInputTokens);

public record AiMetricsTimeBucket(
    DateTimeOffset Bucket,
    string Provider,
    decimal CostUsd,
    int Calls,
    int Tokens);

public record AiMetricsBreakdownRow(
    string Key,
    int Calls,
    decimal CostUsd,
    int Tokens);

public record AiRequestLogMetrics(
    AiMetricsTotals Totals,
    List<AiMetricsTimeBucket> TimeSeries,
    List<AiMetricsBreakdownRow> ByModel,
    List<AiMetricsBreakdownRow> ByWorker,
    List<AiMetricsBreakdownRow> ByProvider);
