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

    public const string GetById = """
        SELECT "Id", "Timestamp", "Worker", "Provider", "Operation", "Model",
               "InputTokens", "OutputTokens", "CacheCreationInputTokens", "CacheReadInputTokens",
               "TotalTokens", "CostUsd", "LatencyMs", "Status", "ErrorMessage",
               "CorrelationId", "ArticleId"
        FROM ai_request_log
        WHERE "Id" = @id
        """;

    public const string GetPaged = """
        SELECT "Id", "Timestamp", "Worker", "Provider", "Operation", "Model",
               "InputTokens", "OutputTokens", "CacheCreationInputTokens", "CacheReadInputTokens",
               "TotalTokens", "CostUsd", "LatencyMs", "Status", "ErrorMessage",
               "CorrelationId", "ArticleId"
        FROM ai_request_log
        WHERE {0}
        ORDER BY "Timestamp" DESC
        LIMIT @pageSize OFFSET @offset
        """;

    public const string Count = """
        SELECT COUNT(*) FROM ai_request_log
        WHERE {0}
        """;

    private const string MetricsKpiFragment = """
        SELECT
            COALESCE(SUM("CostUsd"), 0)                                          AS "TotalCostUsd",
            COUNT(*)                                                              AS "TotalCalls",
            COUNT(*) FILTER (WHERE "Status" = 'Success')                         AS "SuccessCalls",
            COUNT(*) FILTER (WHERE "Status" = 'Error')                           AS "ErrorCalls",
            COALESCE(AVG("LatencyMs"), 0)                                        AS "AverageLatencyMs",
            COALESCE(SUM("InputTokens"), 0)                                      AS "TotalInputTokens",
            COALESCE(SUM("OutputTokens"), 0)                                     AS "TotalOutputTokens",
            COALESCE(SUM("CacheCreationInputTokens"), 0)                         AS "TotalCacheCreationInputTokens",
            COALESCE(SUM("CacheReadInputTokens"), 0)                             AS "TotalCacheReadInputTokens"
        FROM ai_request_log
        WHERE {0};
        """;

    private const string MetricsTimeSeriesFragment = """
        SELECT
            date_trunc('day', "Timestamp")          AS "Bucket",
            "Provider"                              AS "Provider",
            COALESCE(SUM("CostUsd"), 0)             AS "CostUsd",
            COUNT(*)                                AS "Calls",
            COALESCE(SUM("TotalTokens"), 0)         AS "Tokens"
        FROM ai_request_log
        WHERE {0}
        GROUP BY "Bucket", "Provider"
        ORDER BY "Bucket" ASC, "Provider" ASC;
        """;

    private const string MetricsByModelFragment = """
        SELECT
            "Model"                                 AS "Key",
            COUNT(*)                                AS "Calls",
            COALESCE(SUM("CostUsd"), 0)             AS "CostUsd",
            COALESCE(SUM("TotalTokens"), 0)         AS "Tokens"
        FROM ai_request_log
        WHERE {0}
        GROUP BY "Model"
        ORDER BY "Calls" DESC;
        """;

    private const string MetricsByWorkerFragment = """
        SELECT
            "Worker"                                AS "Key",
            COUNT(*)                                AS "Calls",
            COALESCE(SUM("CostUsd"), 0)             AS "CostUsd",
            COALESCE(SUM("TotalTokens"), 0)         AS "Tokens"
        FROM ai_request_log
        WHERE {0}
        GROUP BY "Worker"
        ORDER BY "Calls" DESC;
        """;

    private const string MetricsByProviderFragment = """
        SELECT
            "Provider"                              AS "Key",
            COUNT(*)                                AS "Calls",
            COALESCE(SUM("CostUsd"), 0)             AS "CostUsd",
            COALESCE(SUM("TotalTokens"), 0)         AS "Tokens"
        FROM ai_request_log
        WHERE {0}
        GROUP BY "Provider"
        ORDER BY "Calls" DESC;
        """;

    public static readonly string Metrics =
        MetricsKpiFragment + Environment.NewLine +
        MetricsTimeSeriesFragment + Environment.NewLine +
        MetricsByModelFragment + Environment.NewLine +
        MetricsByWorkerFragment + Environment.NewLine +
        MetricsByProviderFragment;
}
