namespace Infrastructure.Persistence.Repositories.Sql;

internal static class SourceSql
{
    public const string GetActive = """
        SELECT s."Id", s."Name", s."Url", s."Type", s."ProjectId", s."IsActive", s."LastFetchedAt"
        FROM sources s
        JOIN projects p ON p."Id" = s."ProjectId" AND p."IsActive" = TRUE
        WHERE s."IsActive" = true AND s."Type" = @type
        """;

    public const string GetAll = """
        SELECT "Id", "Name", "Url", "Type", "ProjectId", "IsActive", "LastFetchedAt"
        FROM sources
        ORDER BY "Name"
        """;

    public const string GetAllByProject = """
        SELECT "Id", "Name", "Url", "Type", "ProjectId", "IsActive", "LastFetchedAt"
        FROM sources
        WHERE "ProjectId" = @projectId
        ORDER BY "Name"
        """;

    public const string GetById = """
        SELECT "Id", "Name", "Url", "Type", "ProjectId", "IsActive", "LastFetchedAt"
        FROM sources
        WHERE "Id" = @id
        LIMIT 1
        """;

    public const string ExistsByUrl = """
        SELECT EXISTS(SELECT 1 FROM sources WHERE "Url" = @url)
        """;

    public const string ExistsByProjectAndUrl = """
        SELECT EXISTS(SELECT 1 FROM sources WHERE "ProjectId" = @projectId AND "Url" = @url)
        """;

    public const string Insert = """
        INSERT INTO sources ("Id", "Name", "Url", "Type", "ProjectId", "IsActive", "LastFetchedAt")
        VALUES (@Id, @Name, @Url, @Type, @ProjectId, @IsActive, @LastFetchedAt)
        """;

    public const string UpdateLastFetchedAt = """
        UPDATE sources SET "LastFetchedAt" = @fetchedAt WHERE "Id" = @sourceId
        """;

    public const string UpdateFields = """
        UPDATE sources
        SET "Name"     = @Name,
            "Url"      = @Url,
            "IsActive" = @IsActive
        WHERE "Id" = @Id
        """;

    public const string Delete = """
        DELETE FROM sources WHERE "Id" = @id
        """;
}
