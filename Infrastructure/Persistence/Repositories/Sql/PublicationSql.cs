namespace Infrastructure.Persistence.Repositories.Sql;

internal static class PublicationSql
{
    private const string PublicationColumns = """
        p."Id", p."ArticleId", p."EditorId", p."PublishTargetId",
        p."GeneratedContent", p."Status", p."CreatedAt", p."PublishedAt",
        p."ApprovedAt", p."EventId", p."ParentPublicationId", p."UpdateContext",
        p."EditorFeedback", p."SelectedMediaFileIds", p."ReviewedByEditorId", p."RejectedAt", p."RejectionReason"
        """;

    private const string PublishTargetColumns = """
        t."Id", t."Name", t."Platform", t."Identifier", t."SystemPrompt", t."IsActive"
        """;

    private const string ArticleColumns = """
        a."Id", a."OriginalContent", a."SourceId", a."OriginalUrl", a."PublishedAt",
        a."ExternalId", a."Embedding", a."Title", a."Tags", a."Category", a."Sentiment",
        a."ProcessedAt", a."Status", a."ModelVersion", a."Language", a."Summary",
        a."KeyFacts", a."RejectionReason", a."RetryCount", a."EventId", a."Role",
        a."WasReclassified", a."AddedToEventAt"
        """;

    private const string EventColumns = """
        e."Id", e."Title", e."Summary", e."Status", e."FirstSeenAt", e."LastUpdatedAt",
        e."Embedding", e."ArticleCount"
        """;

    public const string GetPendingForGeneration = """
        SELECT p."Id", p."ArticleId", p."EditorId", p."PublishTargetId",
               p."GeneratedContent", p."Status", p."CreatedAt", p."PublishedAt",
               p."ApprovedAt", p."EventId", p."ParentPublicationId", p."UpdateContext",
               p."EditorFeedback", p."SelectedMediaFileIds", p."ReviewedByEditorId", p."RejectedAt", p."RejectionReason",
               a."Id", a."OriginalContent", a."SourceId", a."OriginalUrl", a."PublishedAt",
               a."ExternalId", a."Embedding", a."Title", a."Tags", a."Category", a."Sentiment",
               a."ProcessedAt", a."Status", a."ModelVersion", a."Language", a."Summary",
               a."KeyFacts", a."RejectionReason", a."RetryCount", a."EventId", a."Role",
               a."WasReclassified", a."AddedToEventAt",
               t."Id", t."Name", t."Platform", t."Identifier", t."SystemPrompt", t."IsActive",
               e."Id", e."Title", e."Summary", e."Status", e."FirstSeenAt", e."LastUpdatedAt",
               e."Embedding", e."ArticleCount"
        FROM publications p
        INNER JOIN articles a ON a."Id" = p."ArticleId"
        INNER JOIN publish_targets t ON t."Id" = p."PublishTargetId"
        LEFT JOIN events e ON e."Id" = p."EventId"
        WHERE p."Status" = 'Created'
        ORDER BY p."CreatedAt"
        LIMIT @batchSize
        FOR UPDATE OF p SKIP LOCKED
        """;

    public const string GetEventArticlesByEventIds = """
        SELECT "Id", "OriginalContent", "SourceId", "OriginalUrl", "PublishedAt", "ExternalId",
               "Embedding", "Title", "Tags", "Category", "Sentiment", "ProcessedAt",
               "Status", "ModelVersion", "Language", "Summary", "KeyFacts",
               "RejectionReason", "RetryCount", "EventId", "Role", "WasReclassified", "AddedToEventAt"
        FROM articles WHERE "EventId" = ANY(@eventIds)
        """;

    public const string GetPendingForPublish = """
        SELECT p."Id", p."ArticleId", p."EditorId", p."PublishTargetId",
               p."GeneratedContent", p."Status", p."CreatedAt", p."PublishedAt",
               p."ApprovedAt", p."EventId", p."ParentPublicationId", p."UpdateContext",
               p."EditorFeedback", p."SelectedMediaFileIds", p."ReviewedByEditorId", p."RejectedAt", p."RejectionReason",
               t."Id", t."Name", t."Platform", t."Identifier", t."SystemPrompt", t."IsActive",
               a."Id", a."OriginalContent", a."SourceId", a."OriginalUrl", a."PublishedAt",
               a."ExternalId", a."Embedding", a."Title", a."Tags", a."Category", a."Sentiment",
               a."ProcessedAt", a."Status", a."ModelVersion", a."Language", a."Summary",
               a."KeyFacts", a."RejectionReason", a."RetryCount", a."EventId", a."Role",
               a."WasReclassified", a."AddedToEventAt"
        FROM publications p
        INNER JOIN publish_targets t ON t."Id" = p."PublishTargetId"
        INNER JOIN articles a ON a."Id" = p."ArticleId"
        WHERE p."Status" = 'Approved'
        ORDER BY p."CreatedAt"
        LIMIT @batchSize
        FOR UPDATE OF p SKIP LOCKED
        """;

    public const string Insert = """
        INSERT INTO publications (
            "Id", "ArticleId", "EditorId", "PublishTargetId",
            "GeneratedContent", "Status", "CreatedAt", "PublishedAt",
            "ApprovedAt", "EventId", "ParentPublicationId", "UpdateContext",
            "EditorFeedback", "SelectedMediaFileIds", "ReviewedByEditorId", "RejectedAt", "RejectionReason"
        ) VALUES (
            @Id, @ArticleId, @EditorId, @PublishTargetId,
            @GeneratedContent, @Status, @CreatedAt, @PublishedAt,
            @ApprovedAt, @EventId, @ParentPublicationId, @UpdateContext,
            @EditorFeedback, @SelectedMediaFileIds, @ReviewedByEditorId, @RejectedAt, @RejectionReason
        )
        """;

    public const string GetById = """
        SELECT p."Id", p."ArticleId", p."EditorId", p."PublishTargetId",
               p."GeneratedContent", p."Status", p."CreatedAt", p."PublishedAt",
               p."ApprovedAt", p."EventId", p."ParentPublicationId", p."UpdateContext",
               p."EditorFeedback", p."SelectedMediaFileIds", p."ReviewedByEditorId", p."RejectedAt", p."RejectionReason",
               t."Id", t."Name", t."Platform", t."Identifier", t."SystemPrompt", t."IsActive"
        FROM publications p
        INNER JOIN publish_targets t ON t."Id" = p."PublishTargetId"
        WHERE p."Id" = @id
        LIMIT 1
        """;

    public const string GetDetailPublicationWithTarget = """
        SELECT p."Id", p."ArticleId", p."EditorId", p."PublishTargetId",
               p."GeneratedContent", p."Status", p."CreatedAt", p."PublishedAt",
               p."ApprovedAt", p."EventId", p."ParentPublicationId", p."UpdateContext",
               p."EditorFeedback", p."SelectedMediaFileIds", p."ReviewedByEditorId", p."RejectedAt", p."RejectionReason",
               t."Id", t."Name", t."Platform", t."Identifier", t."SystemPrompt", t."IsActive"
        FROM publications p
        INNER JOIN publish_targets t ON t."Id" = p."PublishTargetId"
        WHERE p."Id" = @id
        LIMIT 1
        """;

    public const string GetDetailPublishLogs = """
        SELECT "Id", "PublicationId", "Status", "ErrorMessage", "AttemptedAt", "ExternalMessageId"
        FROM publish_logs WHERE "PublicationId" = @id
        """;

    public const string GetDetailEvent = """
        SELECT "Id", "Title", "Summary", "Status", "FirstSeenAt", "LastUpdatedAt", "Embedding", "ArticleCount"
        FROM events WHERE "Id" = @eventId
        """;

    public const string GetDetailEventArticles = """
        SELECT "Id", "OriginalContent", "SourceId", "OriginalUrl", "PublishedAt", "ExternalId",
               "Embedding", "Title", "Tags", "Category", "Sentiment", "ProcessedAt",
               "Status", "ModelVersion", "Language", "Summary", "KeyFacts",
               "RejectionReason", "RetryCount", "EventId", "Role", "WasReclassified", "AddedToEventAt"
        FROM articles WHERE "EventId" = @eventId
        """;

    public const string GetDetailMediaFiles = """
        SELECT "Id", "ArticleId", "R2Key", "OriginalUrl", "ContentType", "SizeBytes", "Kind", "CreatedAt"
        FROM media_files WHERE "ArticleId" = ANY(@articleIds)
        """;

    public const string GetByEventId = """
        SELECT p."Id", p."ArticleId", p."EditorId", p."PublishTargetId",
               p."GeneratedContent", p."Status", p."CreatedAt", p."PublishedAt",
               p."ApprovedAt", p."EventId", p."ParentPublicationId", p."UpdateContext",
               p."EditorFeedback", p."SelectedMediaFileIds", p."ReviewedByEditorId", p."RejectedAt", p."RejectionReason",
               t."Id", t."Name", t."Platform", t."Identifier", t."SystemPrompt", t."IsActive"
        FROM publications p
        INNER JOIN publish_targets t ON t."Id" = p."PublishTargetId"
        WHERE p."EventId" = @eventId
        ORDER BY p."CreatedAt"
        """;

    public const string GetAll = """
        SELECT p."Id", p."ArticleId", p."EditorId", p."PublishTargetId",
               p."GeneratedContent", p."Status", p."CreatedAt", p."PublishedAt",
               p."ApprovedAt", p."EventId", p."ParentPublicationId", p."UpdateContext",
               p."EditorFeedback", p."SelectedMediaFileIds", p."ReviewedByEditorId", p."RejectedAt", p."RejectionReason",
               t."Id", t."Name", t."Platform", t."Identifier", t."SystemPrompt", t."IsActive",
               e."Id", e."Title", e."Summary", e."Status", e."FirstSeenAt", e."LastUpdatedAt",
               e."Embedding", e."ArticleCount"
        FROM publications p
        INNER JOIN publish_targets t ON t."Id" = p."PublishTargetId"
        LEFT JOIN events e ON e."Id" = p."EventId"
        ORDER BY p."CreatedAt" DESC
        LIMIT @pageSize OFFSET @offset
        """;

    public const string CountAll = """
        SELECT COUNT(*) FROM publications
        """;

    public const string UpdateStatus = """
        UPDATE publications SET "Status" = @status WHERE "Id" = @id
        """;

    public const string UpdateGeneratedContent = """
        UPDATE publications SET "GeneratedContent" = @content WHERE "Id" = @id
        """;

    public const string UpdatePublishedAt = """
        UPDATE publications SET "PublishedAt" = @publishedAt WHERE "Id" = @id
        """;

    public const string UpdateContentAndMedia = """
        UPDATE publications SET "GeneratedContent" = @content, "SelectedMediaFileIds" = @selectedMediaFileIds::jsonb WHERE "Id" = @id
        """;

    public const string UpdateApproval = """
        UPDATE publications
        SET "Status"              = 'Approved',
            "ApprovedAt"          = @approvedAt,
            "ReviewedByEditorId"  = @editorId
        WHERE "Id" = @id
        """;

    public const string UpdateRejection = """
        UPDATE publications
        SET "Status"             = 'Rejected',
            "RejectedAt"         = @rejectedAt,
            "RejectionReason"    = @reason,
            "ReviewedByEditorId" = @editorId
        WHERE "Id" = @id
        """;

    public const string InsertPublishLog = """
        INSERT INTO publish_logs ("Id", "PublicationId", "Status", "ErrorMessage", "AttemptedAt", "ExternalMessageId")
        VALUES (@Id, @PublicationId, @Status, @ErrorMessage, @AttemptedAt, @ExternalMessageId)
        """;

    public const string GetExternalMessageId = """
        SELECT "ExternalMessageId" FROM publish_logs
        WHERE "PublicationId" = @publicationId
          AND "Status" = 'Success'
          AND "ExternalMessageId" IS NOT NULL
        ORDER BY "AttemptedAt" DESC
        LIMIT 1
        """;

    public const string GetOriginalEventPublication = """
        SELECT p."Id", p."ArticleId", p."EditorId", p."PublishTargetId",
               p."GeneratedContent", p."Status", p."CreatedAt", p."PublishedAt",
               p."ApprovedAt", p."EventId", p."ParentPublicationId", p."UpdateContext",
               p."EditorFeedback", p."SelectedMediaFileIds", p."ReviewedByEditorId", p."RejectedAt", p."RejectionReason",
               t."Id", t."Name", t."Platform", t."Identifier", t."SystemPrompt", t."IsActive"
        FROM publications p
        INNER JOIN publish_targets t ON t."Id" = p."PublishTargetId"
        WHERE p."EventId" = @eventId
          AND p."ParentPublicationId" IS NULL
          AND p."Status" = 'Published'
        ORDER BY p."CreatedAt"
        LIMIT 1
        """;

    public const string RequestRegeneration = """
        UPDATE publications
        SET "Status"           = 'Created',
            "EditorFeedback"   = @feedback,
            "GeneratedContent" = ''
        WHERE "Id" = @id
        """;
}
