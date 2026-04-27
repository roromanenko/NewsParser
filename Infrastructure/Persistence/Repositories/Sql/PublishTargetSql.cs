namespace Infrastructure.Persistence.Repositories.Sql;

internal static class PublishTargetSql
{
    public const string GetAll = """
        SELECT "Id", "Name", "Platform", "Identifier", "SystemPrompt", "IsActive", "ProjectId"
        FROM publish_targets
        ORDER BY "Name"
        """;

    public const string GetActive = """
        SELECT "Id", "Name", "Platform", "Identifier", "SystemPrompt", "IsActive", "ProjectId"
        FROM publish_targets
        WHERE "IsActive" = true
        ORDER BY "Name"
        """;

    public const string GetById = """
        SELECT "Id", "Name", "Platform", "Identifier", "SystemPrompt", "IsActive", "ProjectId"
        FROM publish_targets
        WHERE "Id" = @id
        LIMIT 1
        """;

    public const string GetAllByProject = """
        SELECT "Id", "Name", "Platform", "Identifier", "SystemPrompt", "IsActive", "ProjectId"
        FROM publish_targets
        WHERE "ProjectId" = @projectId
        ORDER BY "Name"
        """;

    public const string GetActiveByProject = """
        SELECT "Id", "Name", "Platform", "Identifier", "SystemPrompt", "IsActive", "ProjectId"
        FROM publish_targets
        WHERE "ProjectId" = @projectId AND "IsActive" = true
        ORDER BY "Name"
        """;

    public const string Insert = """
        INSERT INTO publish_targets ("Id", "Name", "Platform", "Identifier", "SystemPrompt", "IsActive", "ProjectId")
        VALUES (@Id, @Name, @Platform, @Identifier, @SystemPrompt, @IsActive, @ProjectId)
        """;

    public const string Update = """
        UPDATE publish_targets
        SET "Name"         = @Name,
            "Identifier"   = @Identifier,
            "SystemPrompt" = @SystemPrompt,
            "IsActive"     = @IsActive
        WHERE "Id" = @Id
        """;

    public const string Delete = """
        DELETE FROM publish_targets WHERE "Id" = @id
        """;
}
