using DbUp;

namespace Infrastructure.Persistence.Migrator;

internal static class DbUpMigrator
{
    public static void Migrate(string connectionString)
    {
        var result = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrator).Assembly)
			.WithVariablesDisabled()
			.LogToConsole()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
            throw new InvalidOperationException("DB migration failed", result.Error);
    }
}
