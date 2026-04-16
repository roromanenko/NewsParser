using Microsoft.Extensions.Configuration;
using Npgsql;
using Pgvector.Npgsql;

namespace Infrastructure.Persistence.Connection;

internal sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlConnectionFactory(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("NewsParserDbContext")
            ?? throw new InvalidOperationException("Connection string 'NewsParserDbContext' is not configured.");

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        _dataSource = builder.Build();
    }

    public NpgsqlConnection Create() => _dataSource.CreateConnection();

    public async Task<NpgsqlConnection> CreateOpenAsync(CancellationToken ct) =>
        await _dataSource.OpenConnectionAsync(ct);
}
