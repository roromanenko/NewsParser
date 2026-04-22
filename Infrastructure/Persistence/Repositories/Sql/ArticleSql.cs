namespace Infrastructure.Persistence.Repositories.Sql;

internal static class ArticleSql
{
    public const string Insert = """
        INSERT INTO articles (
            "Id", "OriginalContent", "SourceId", "OriginalUrl", "PublishedAt", "ExternalId",
            "Embedding", "Title", "Tags", "Category", "Sentiment", "ProcessedAt",
            "Status", "ModelVersion", "Language", "Summary", "KeyFacts",
            "RejectionReason", "RetryCount", "EventId", "Role", "WasReclassified", "AddedToEventAt"
        ) VALUES (
            @Id, @OriginalContent, @SourceId, @OriginalUrl, @PublishedAt, @ExternalId,
            @Embedding, @Title, @Tags, @Category, @Sentiment, @ProcessedAt,
            @Status, @ModelVersion, @Language, @Summary, @KeyFacts,
            @RejectionReason, @RetryCount, @EventId, @Role, @WasReclassified, @AddedToEventAt
        )
        """;

    public const string GetById = """
        SELECT a."Id", a."OriginalContent", a."SourceId", a."OriginalUrl", a."PublishedAt",
               a."ExternalId", a."Embedding", a."Title", a."Tags", a."Category", a."Sentiment",
               a."ProcessedAt", a."Status", a."ModelVersion", a."Language", a."Summary",
               a."KeyFacts", a."RejectionReason", a."RetryCount", a."EventId", a."Role",
               a."WasReclassified", a."AddedToEventAt",
               m."Id", m."ArticleId", m."PublicationId", m."OwnerKind", m."UploadedByUserId",
               m."R2Key", m."OriginalUrl", m."ContentType", m."SizeBytes", m."Kind", m."CreatedAt"
        FROM articles a
        LEFT JOIN media_files m ON m."ArticleId" = a."Id"
        WHERE a."Id" = @id
        """;

    public const string GetAnalysisDoneWithSearch = """
        SELECT "Id", "OriginalContent", "SourceId", "OriginalUrl", "PublishedAt", "ExternalId",
               "Embedding", "Title", "Tags", "Category", "Sentiment", "ProcessedAt",
               "Status", "ModelVersion", "Language", "Summary", "KeyFacts",
               "RejectionReason", "RetryCount", "EventId", "Role", "WasReclassified", "AddedToEventAt"
        FROM articles
        WHERE "Status" = 'AnalysisDone'
          AND ("Title" ILIKE @pattern ESCAPE '\' OR "Summary" ILIKE @pattern ESCAPE '\')
        ORDER BY "ProcessedAt" {0}
        LIMIT @pageSize OFFSET @offset
        """;

    public const string GetAnalysisDoneWithoutSearch = """
        SELECT "Id", "OriginalContent", "SourceId", "OriginalUrl", "PublishedAt", "ExternalId",
               "Embedding", "Title", "Tags", "Category", "Sentiment", "ProcessedAt",
               "Status", "ModelVersion", "Language", "Summary", "KeyFacts",
               "RejectionReason", "RetryCount", "EventId", "Role", "WasReclassified", "AddedToEventAt"
        FROM articles
        WHERE "Status" = 'AnalysisDone'
        ORDER BY "ProcessedAt" {0}
        LIMIT @pageSize OFFSET @offset
        """;

    public const string CountAnalysisDoneWithSearch = """
        SELECT COUNT(*) FROM articles
        WHERE "Status" = 'AnalysisDone'
          AND ("Title" ILIKE @pattern ESCAPE '\' OR "Summary" ILIKE @pattern ESCAPE '\')
        """;

    public const string CountAnalysisDoneWithoutSearch = """
        SELECT COUNT(*) FROM articles
        WHERE "Status" = 'AnalysisDone'
        """;

    public const string UpdateStatus = """
        UPDATE articles SET "Status" = @status WHERE "Id" = @id
        """;

    public const string Reject = """
        UPDATE articles SET "Status" = 'Rejected', "RejectionReason" = @reason WHERE "Id" = @id
        """;

    public const string IncrementRetry = """
        UPDATE articles SET "RetryCount" = "RetryCount" + 1 WHERE "Id" = @id
        """;

    public const string GetPending = """
        SELECT "Id", "OriginalContent", "SourceId", "OriginalUrl", "PublishedAt", "ExternalId",
               "Embedding", "Title", "Tags", "Category", "Sentiment", "ProcessedAt",
               "Status", "ModelVersion", "Language", "Summary", "KeyFacts",
               "RejectionReason", "RetryCount", "EventId", "Role", "WasReclassified", "AddedToEventAt"
        FROM articles
        WHERE "Status" = 'Pending'
        ORDER BY "ProcessedAt"
        LIMIT @batchSize
        FOR UPDATE SKIP LOCKED
        """;

    public const string GetPendingForClassification = """
        SELECT "Id", "OriginalContent", "SourceId", "OriginalUrl", "PublishedAt", "ExternalId",
               "Embedding", "Title", "Tags", "Category", "Sentiment", "ProcessedAt",
               "Status", "ModelVersion", "Language", "Summary", "KeyFacts",
               "RejectionReason", "RetryCount", "EventId", "Role", "WasReclassified", "AddedToEventAt"
        FROM articles
        WHERE "Status" = 'AnalysisDone' AND "EventId" IS NULL
        ORDER BY "ProcessedAt"
        LIMIT @batchSize
        FOR UPDATE SKIP LOCKED
        """;

    public const string UpdateKeyFacts = """
        UPDATE articles SET "KeyFacts" = @keyFacts::jsonb WHERE "Id" = @id
        """;

    public const string UpdateAnalysisResult = """
        UPDATE articles
        SET "Category"     = @category,
            "Tags"         = @tags,
            "Sentiment"    = @sentiment,
            "Language"     = @language,
            "Summary"      = @summary,
            "ModelVersion" = @modelVersion
        WHERE "Id" = @id
        """;

    public const string UpdateEmbedding = """
        UPDATE articles SET "Embedding" = @embedding WHERE "Id" = @id
        """;

    public const string ExistsBySourceAndExternal = """
        SELECT EXISTS(SELECT 1 FROM articles WHERE "SourceId" = @sourceId AND "ExternalId" = @externalId)
        """;

    public const string ExistsByUrl = """
        SELECT EXISTS(
            SELECT 1 FROM articles
            WHERE "OriginalUrl" = @url AND "Status" <> 'Rejected'
        )
        """;

    public const string GetRecentTitlesForDeduplication = """
        SELECT "Title" FROM articles
        WHERE "PublishedAt" >= @since AND "Status" <> 'Rejected'
        """;
}
