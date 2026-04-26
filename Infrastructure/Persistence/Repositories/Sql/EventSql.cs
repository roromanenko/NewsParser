namespace Infrastructure.Persistence.Repositories.Sql;

internal static class EventSql
{
    // Sort key constants — kept in sync with Api.Controllers.SortOptions.EventSortValues
    internal static class SortKeys
    {
        public const string Newest = "newest";
        public const string Oldest = "oldest";
        public const string Importance = "importance";
    }

    // Tier-filter SQL fragments — inserted via string.Format into paged/count queries
    // Paged query: column is prefixed with the table alias e.
    public const string TierFilterPaged = """AND e."ImportanceTier" = @tier""";

    // Count query: no table alias
    public const string TierFilterCount = """AND "ImportanceTier" = @tier""";


    private const string EventColumns = """
        "Id", "Title", "Summary", "Status", "FirstSeenAt", "LastUpdatedAt", "Embedding", "ArticleCount",
        "ImportanceTier", "ImportanceBaseScore", "ImportanceCalculatedAt", "ProjectId"
        """;

    public const string GetById = $"""
        SELECT {EventColumns}
        FROM events WHERE "Id" = @id
        """;

    public const string GetArticlesByEventId = """
        SELECT "Id", "OriginalContent", "SourceId", "OriginalUrl", "PublishedAt", "ExternalId",
               "Embedding", "Title", "Tags", "Category", "Sentiment", "ProcessedAt",
               "Status", "ModelVersion", "Language", "Summary", "KeyFacts",
               "RejectionReason", "RetryCount", "EventId", "Role", "WasReclassified", "AddedToEventAt",
               "ProjectId"
        FROM articles WHERE "EventId" = @id
        """;

    public const string GetEventUpdatesByEventId = """
        SELECT "Id", "EventId", "ArticleId", "FactSummary", "IsPublished", "CreatedAt"
        FROM event_updates WHERE "EventId" = @id
        """;

    public const string GetContradictionsByEventId = """
        SELECT "Id", "EventId", "Description", "IsResolved", "CreatedAt"
        FROM contradictions WHERE "EventId" = @id
        """;

    public const string GetContradictionArticlesByContradictionIds = """
        SELECT "ContradictionId", "ArticleId"
        FROM contradiction_articles WHERE "ContradictionId" = ANY(@ids)
        """;

    public const string GetMediaFilesByArticleIds = """
        SELECT "Id", "ArticleId", "PublicationId", "OwnerKind", "UploadedByUserId",
               "R2Key", "OriginalUrl", "ContentType", "SizeBytes", "Kind", "CreatedAt"
        FROM media_files WHERE "ArticleId" = ANY(@ids)
        """;

    public const string GetActiveEvents = $"""
        SELECT {EventColumns}
        FROM events WHERE "Status" = 'Active' AND "ProjectId" = @projectId
        ORDER BY "LastUpdatedAt" DESC
        """;

    public const string FindSimilarEvents = $"""
        SELECT {EventColumns},
               1 - ("Embedding" <=> @vector::vector) AS similarity
        FROM events
        WHERE "ProjectId" = @projectId
          AND "Status" = 'Active'
          AND "LastUpdatedAt" >= @windowStart
          AND "Embedding" IS NOT NULL
          AND (1 - ("Embedding" <=> @vector::vector)) >= @threshold
        ORDER BY similarity DESC
        LIMIT @maxTake
        """;

    public const string Insert = """
        INSERT INTO events ("Id", "Title", "Summary", "Status", "FirstSeenAt", "LastUpdatedAt", "Embedding", "ArticleCount", "ProjectId")
        VALUES (@Id, @Title, @Summary, @Status, @FirstSeenAt, @LastUpdatedAt, @Embedding, @ArticleCount, @ProjectId)
        """;

    public const string UpdateSummaryTitleAndEmbedding = """
        UPDATE events
        SET "Title"        = @title,
            "Summary"      = @summary,
            "Embedding"    = @embedding,
            "ArticleCount" = "ArticleCount" + 1,
            "LastUpdatedAt"= @lastUpdatedAt
        WHERE "Id" = @id
        """;

    public const string UpdateLastUpdatedAt = """
        UPDATE events SET "LastUpdatedAt" = @lastUpdatedAt WHERE "Id" = @id
        """;

    public const string AssignArticleToEvent = """
        UPDATE articles
        SET "EventId"       = @eventId,
            "Role"          = @role,
            "AddedToEventAt"= @addedToEventAt
        WHERE "Id" = @articleId
        """;

    public const string InsertEventUpdate = """
        INSERT INTO event_updates ("Id", "EventId", "ArticleId", "FactSummary", "IsPublished", "CreatedAt")
        VALUES (@Id, @EventId, @ArticleId, @FactSummary, @IsPublished, @CreatedAt)
        """;

    public const string InsertContradiction = """
        INSERT INTO contradictions ("Id", "EventId", "Description", "IsResolved", "CreatedAt")
        VALUES (@Id, @EventId, @Description, @IsResolved, @CreatedAt)
        """;

    public const string InsertContradictionArticle = """
        INSERT INTO contradiction_articles ("ContradictionId", "ArticleId") VALUES (@ContradictionId, @ArticleId)
        """;

    public const string GetUnpublishedUpdates = """
        SELECT eu."Id", eu."EventId", eu."ArticleId", eu."FactSummary", eu."IsPublished", eu."CreatedAt"
        FROM event_updates eu
        WHERE eu."IsPublished" = false
        ORDER BY eu."CreatedAt"
        LIMIT @batchSize
        """;

    public const string GetUnpublishedUpdateEvents = $"""
        SELECT {EventColumns}
        FROM events e
        WHERE e."Id" = ANY(@eventIds)
        """;

    public const string GetUnpublishedUpdateArticles = """
        SELECT "Id", "OriginalContent", "SourceId", "OriginalUrl", "PublishedAt", "ExternalId",
               "Embedding", "Title", "Tags", "Category", "Sentiment", "ProcessedAt",
               "Status", "ModelVersion", "Language", "Summary", "KeyFacts",
               "RejectionReason", "RetryCount", "EventId", "Role", "WasReclassified", "AddedToEventAt",
               "ProjectId"
        FROM articles WHERE "Id" = ANY(@articleIds)
        """;

    public const string MarkUpdatePublished = """
        UPDATE event_updates SET "IsPublished" = true WHERE "Id" = @eventUpdateId
        """;

    public const string CountUpdatesFrom = """
        SELECT COUNT(*) FROM event_updates WHERE "EventId" = @eventId AND "CreatedAt" >= @from
        """;

    public const string GetLastUpdateTime = """
        SELECT "CreatedAt" FROM event_updates WHERE "EventId" = @eventId ORDER BY "CreatedAt" DESC LIMIT 1
        """;

    // {0} = ORDER BY clause, {1} = optional tier filter clause (e.g. "AND \"ImportanceTier\" = @tier")
    public const string GetPagedWithSearch = """
        SELECT e."Id", e."Title", e."Summary", e."Status", e."FirstSeenAt", e."LastUpdatedAt",
               e."Embedding", e."ArticleCount",
               e."ImportanceTier", e."ImportanceBaseScore", e."ImportanceCalculatedAt",
               e."ProjectId",
               COUNT(DISTINCT a."SourceId") AS "DistinctSourceCount"
        FROM events e
        LEFT JOIN articles a ON a."EventId" = e."Id"
        WHERE e."ProjectId" = @projectId
          AND (e."Title" ILIKE @pattern ESCAPE '\' OR e."Summary" ILIKE @pattern ESCAPE '\')
          {1}
        GROUP BY e."Id"
        {0}
        LIMIT @pageSize OFFSET @offset
        """;

    public const string GetPagedWithoutSearch = """
        SELECT e."Id", e."Title", e."Summary", e."Status", e."FirstSeenAt", e."LastUpdatedAt",
               e."Embedding", e."ArticleCount",
               e."ImportanceTier", e."ImportanceBaseScore", e."ImportanceCalculatedAt",
               e."ProjectId",
               COUNT(DISTINCT a."SourceId") AS "DistinctSourceCount"
        FROM events e
        LEFT JOIN articles a ON a."EventId" = e."Id"
        WHERE e."ProjectId" = @projectId
          {1}
        GROUP BY e."Id"
        {0}
        LIMIT @pageSize OFFSET @offset
        """;

    // ORDER BY clause for importance sort using live decayed score
    public const string ImportanceOrderBy = """
        ORDER BY e."ImportanceBaseScore"
          * POWER(0.5, EXTRACT(EPOCH FROM (NOW() - GREATEST(e."LastUpdatedAt", e."ImportanceCalculatedAt"))) / 3600.0 / @halfLifeHours)
          DESC NULLS LAST
        """;

    // {0} = optional tier filter clause
    public const string CountWithSearch = """
        SELECT COUNT(*) FROM events
        WHERE "ProjectId" = @projectId
          AND ("Title" ILIKE @pattern ESCAPE '\' OR "Summary" ILIKE @pattern ESCAPE '\')
          {0}
        """;

    public const string CountWithoutSearch = """
        SELECT COUNT(*) FROM events
        WHERE "ProjectId" = @projectId
          {0}
        """;

    public const string ResolveContradiction = """
        UPDATE contradictions SET "IsResolved" = true WHERE "Id" = @contradictionId
        """;

    public const string MergeArticles = """
        UPDATE articles SET "EventId" = @targetEventId WHERE "EventId" = @sourceEventId
        """;

    public const string MergeEventUpdates = """
        UPDATE event_updates SET "EventId" = @targetEventId WHERE "EventId" = @sourceEventId
        """;

    public const string MergeContradictions = """
        UPDATE contradictions SET "EventId" = @targetEventId WHERE "EventId" = @sourceEventId
        """;

    public const string ArchiveEvent = """
        UPDATE events SET "Status" = 'Archived' WHERE "Id" = @sourceEventId
        """;

    public const string TouchLastUpdatedAt = """
        UPDATE events SET "LastUpdatedAt" = @now WHERE "Id" = @targetEventId
        """;

    public const string UpdateArticleRole = """
        UPDATE articles SET "Role" = @role WHERE "Id" = @articleId
        """;

    public const string UpdateEventStatus = """
        UPDATE events SET "Status" = @status WHERE "Id" = @id
        """;

    public const string MarkArticleReclassified = """
        UPDATE articles SET "WasReclassified" = true WHERE "Id" = @articleId
        """;

    public const string GetArticlesByEventIds = """
        SELECT "Id", "OriginalContent", "SourceId", "OriginalUrl", "PublishedAt", "ExternalId",
               "Embedding", "Title", "Tags", "Category", "Sentiment", "ProcessedAt",
               "Status", "ModelVersion", "Language", "Summary", "KeyFacts",
               "RejectionReason", "RetryCount", "EventId", "Role", "WasReclassified", "AddedToEventAt",
               "ProjectId"
        FROM articles WHERE "EventId" = ANY(@ids)
        """;

    public const string GetContradictionsByEventIds = """
        SELECT "Id", "EventId", "Description", "IsResolved", "CreatedAt"
        FROM contradictions WHERE "EventId" = ANY(@ids)
        """;

    public const string GetImportanceStats = """
        SELECT
          COUNT(*)                                                                   AS "ArticleCount",
          COUNT(DISTINCT "SourceId")                                                 AS "DistinctSourceCount",
          COUNT(*) FILTER (WHERE "AddedToEventAt" >= NOW() - INTERVAL '1 hour')     AS "ArticlesLastHour",
          MAX("AddedToEventAt")                                                      AS "LastArticleAt"
        FROM articles
        WHERE "EventId" = @eventId
        """;

    public const string UpdateImportance = """
        UPDATE events
        SET "ImportanceTier"         = @tier,
            "ImportanceBaseScore"    = @baseScore,
            "ImportanceCalculatedAt" = @calculatedAt
        WHERE "Id" = @eventId
        """;
}
