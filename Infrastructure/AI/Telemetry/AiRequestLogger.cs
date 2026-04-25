using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Telemetry;

internal class AiRequestLogger(
    IAiCostCalculator calculator,
    IAiRequestLogRepository repository,
    ILogger<AiRequestLogger> logger) : IAiRequestLogger
{
    private const int MaxErrorMessageLength = 500;

    public async Task LogAsync(AiRequestLogEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            var cost = calculator.Calculate(entry.Usage, entry.Provider, entry.Model);
            var row = BuildLogRow(entry, cost);
            await repository.AddAsync(row, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Failed to persist AI request log for {Provider} {Model} ({Operation})",
                entry.Provider, entry.Model, entry.Operation);
        }
    }

    private static AiRequestLog BuildLogRow(AiRequestLogEntry entry, decimal cost) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UtcNow,
        Worker = entry.Worker,
        Provider = entry.Provider,
        Operation = entry.Operation,
        Model = entry.Model,
        InputTokens = entry.Usage.InputTokens,
        OutputTokens = entry.Usage.OutputTokens,
        CacheCreationInputTokens = entry.Usage.CacheCreationInputTokens,
        CacheReadInputTokens = entry.Usage.CacheReadInputTokens,
        TotalTokens = entry.Usage.InputTokens + entry.Usage.OutputTokens
                    + entry.Usage.CacheCreationInputTokens + entry.Usage.CacheReadInputTokens,
        CostUsd = cost,
        LatencyMs = entry.LatencyMs,
        Status = entry.Status,
        ErrorMessage = Truncate(entry.ErrorMessage, MaxErrorMessageLength),
        CorrelationId = entry.CorrelationId,
        ArticleId = entry.ArticleId
    };

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value[..Math.Min(value.Length, maxLength)];
}
