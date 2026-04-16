namespace Infrastructure.Persistence.Repositories.Sql;

internal static class PublishTargetSql
{
    public const string GetAll = """
        SELECT "Id", "Name", "Platform", "Identifier", "SystemPrompt", "IsActive"
        FROM publish_targets
        ORDER BY "Name"
        """;

    public const string GetActive = """
        SELECT "Id", "Name", "Platform", "Identifier", "SystemPrompt", "IsActive"
        FROM publish_targets
        WHERE "IsActive" = true
        ORDER BY "Name"
        """;

    public const string GetById = """
        SELECT "Id", "Name", "Platform", "Identifier", "SystemPrompt", "IsActive"
        FROM publish_targets
        WHERE "Id" = @id
        LIMIT 1
        """;

    public const string Insert = """
        INSERT INTO publish_targets ("Id", "Name", "Platform", "Identifier", "SystemPrompt", "IsActive")
        VALUES (@Id, @Name, @Platform, @Identifier, @SystemPrompt, @IsActive)
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
