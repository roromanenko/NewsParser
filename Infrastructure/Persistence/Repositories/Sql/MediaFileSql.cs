namespace Infrastructure.Persistence.Repositories.Sql;

internal static class MediaFileSql
{
    public const string Insert = """
        INSERT INTO media_files ("Id", "ArticleId", "R2Key", "OriginalUrl", "ContentType", "SizeBytes", "Kind", "CreatedAt")
        VALUES (@Id, @ArticleId, @R2Key, @OriginalUrl, @ContentType, @SizeBytes, @Kind, @CreatedAt)
        """;

    public const string GetByArticleId = """
        SELECT "Id", "ArticleId", "R2Key", "OriginalUrl", "ContentType", "SizeBytes", "Kind", "CreatedAt"
        FROM media_files
        WHERE "ArticleId" = @articleId
        """;

    public const string ExistsByArticleAndUrl = """
        SELECT EXISTS(SELECT 1 FROM media_files WHERE "ArticleId" = @articleId AND "OriginalUrl" = @originalUrl)
        """;

    public const string GetByIds = """
        SELECT "Id", "ArticleId", "R2Key", "OriginalUrl", "ContentType", "SizeBytes", "Kind", "CreatedAt"
        FROM media_files
        WHERE "Id" = ANY(@ids)
        """;
}
