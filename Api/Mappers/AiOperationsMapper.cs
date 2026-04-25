using Api.Models;
using Core.DomainModels;

namespace Api.Mappers;

public static class AiOperationsMapper
{
    public static AiRequestLogDto ToDto(this AiRequestLog log) => new(
        log.Id,
        log.Timestamp,
        log.Worker,
        log.Provider,
        log.Operation,
        log.Model,
        log.InputTokens,
        log.OutputTokens,
        log.CacheCreationInputTokens,
        log.CacheReadInputTokens,
        log.TotalTokens,
        log.CostUsd,
        log.LatencyMs,
        log.Status.ToString(),
        log.ErrorMessage,
        log.CorrelationId,
        log.ArticleId);

    public static AiMetricsTimeBucketDto ToDto(this AiMetricsTimeBucket bucket) => new(
        bucket.Bucket,
        bucket.Provider,
        bucket.CostUsd,
        bucket.Calls,
        bucket.Tokens);

    public static AiMetricsBreakdownRowDto ToDto(this AiMetricsBreakdownRow row) => new(
        row.Key,
        row.Calls,
        row.CostUsd,
        row.Tokens);

    public static AiOperationsMetricsDto ToDto(this AiRequestLogMetrics metrics) => new(
        metrics.Totals.TotalCostUsd,
        metrics.Totals.TotalCalls,
        metrics.Totals.SuccessCalls,
        metrics.Totals.ErrorCalls,
        metrics.Totals.AverageLatencyMs,
        metrics.Totals.TotalInputTokens,
        metrics.Totals.TotalOutputTokens,
        metrics.Totals.TotalCacheCreationInputTokens,
        metrics.Totals.TotalCacheReadInputTokens,
        metrics.TimeSeries.Select(t => t.ToDto()).ToList(),
        metrics.ByModel.Select(r => r.ToDto()).ToList(),
        metrics.ByWorker.Select(r => r.ToDto()).ToList(),
        metrics.ByProvider.Select(r => r.ToDto()).ToList());
}
