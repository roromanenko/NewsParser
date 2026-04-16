using Npgsql;

namespace Infrastructure.Persistence.Connection;

internal interface IDbConnectionFactory
{
    NpgsqlConnection Create();
    Task<NpgsqlConnection> CreateOpenAsync(CancellationToken ct);
}
