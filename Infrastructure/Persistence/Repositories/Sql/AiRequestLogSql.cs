namespace Infrastructure.Persistence.Repositories.Sql;

internal static class AiRequestLogSql
{
    public const string Insert = """
        INSERT INTO ai_request_log (
            "Id", "Timestamp", "Worker", "Provider", "Operation", "Model",
            "InputTokens", "OutputTokens", "CacheCreationInputTokens", "CacheReadInputTokens",
            "TotalTokens", "CostUsd", "LatencyMs", "Status", "ErrorMessage",
            "CorrelationId", "ArticleId"
        ) VALUES (
            @Id, @Timestamp, @Worker, @Provider, @Operation, @Model,
            @InputTokens, @OutputTokens, @CacheCreationInputTokens, @CacheReadInputTokens,
            @TotalTokens, @CostUsd, @LatencyMs, @Status, @ErrorMessage,
            @CorrelationId, @ArticleId
        )
        """;
}
