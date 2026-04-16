namespace Infrastructure.Persistence.Repositories.Sql;

internal static class UserSql
{
    public const string GetByEmail = """
        SELECT "Id", "FirstName", "LastName", "Email", "PasswordHash", "Role"
        FROM users
        WHERE "Email" = @email
        LIMIT 1
        """;

    public const string GetById = """
        SELECT "Id", "FirstName", "LastName", "Email", "PasswordHash", "Role"
        FROM users
        WHERE "Id" = @id
        LIMIT 1
        """;

    public const string GetAll = """
        SELECT "Id", "FirstName", "LastName", "Email", "PasswordHash", "Role"
        FROM users
        """;

    public const string ExistsByEmail = """
        SELECT EXISTS(SELECT 1 FROM users WHERE "Email" = @email)
        """;

    public const string Insert = """
        INSERT INTO users ("Id", "FirstName", "LastName", "Email", "PasswordHash", "Role")
        VALUES (@Id, @FirstName, @LastName, @Email, @PasswordHash, @Role)
        """;

    public const string Update = """
        UPDATE users
        SET "FirstName" = @firstName,
            "LastName"  = @lastName,
            "Email"     = @email
        WHERE "Id" = @id
        """;

    public const string Delete = """
        DELETE FROM users WHERE "Id" = @id
        """;
}
