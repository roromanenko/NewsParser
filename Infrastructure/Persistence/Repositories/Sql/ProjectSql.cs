namespace Infrastructure.Persistence.Repositories.Sql;

internal static class ProjectSql
{
    public const string GetById = """
        SELECT "Id", "Name", "Slug", "AnalyzerPromptText", "Categories",
               "OutputLanguage", "OutputLanguageName", "IsActive", "CreatedAt"
        FROM projects
        WHERE "Id" = @id
        LIMIT 1
        """;

    public const string GetAll = """
        SELECT "Id", "Name", "Slug", "AnalyzerPromptText", "Categories",
               "OutputLanguage", "OutputLanguageName", "IsActive", "CreatedAt"
        FROM projects
        ORDER BY "Name"
        """;

    public const string GetBySlug = """
        SELECT "Id", "Name", "Slug", "AnalyzerPromptText", "Categories",
               "OutputLanguage", "OutputLanguageName", "IsActive", "CreatedAt"
        FROM projects
        WHERE "Slug" = @slug
        LIMIT 1
        """;

    public const string Exists = """
        SELECT EXISTS(SELECT 1 FROM projects WHERE "Id" = @id)
        """;

    public const string Insert = """
        INSERT INTO projects (
            "Id", "Name", "Slug", "AnalyzerPromptText", "Categories",
            "OutputLanguage", "OutputLanguageName", "IsActive", "CreatedAt"
        ) VALUES (
            @Id, @Name, @Slug, @AnalyzerPromptText, @Categories,
            @OutputLanguage, @OutputLanguageName, @IsActive, @CreatedAt
        )
        """;

    public const string Update = """
        UPDATE projects
        SET "Name"               = @Name,
            "Slug"               = @Slug,
            "AnalyzerPromptText" = @AnalyzerPromptText,
            "Categories"         = @Categories,
            "OutputLanguage"     = @OutputLanguage,
            "OutputLanguageName" = @OutputLanguageName,
            "IsActive"           = @IsActive
        WHERE "Id" = @Id
        """;

    public const string UpdateActive = """
        UPDATE projects SET "IsActive" = @isActive WHERE "Id" = @id
        """;

    public const string Delete = """
        DELETE FROM projects WHERE "Id" = @id
        """;
}
