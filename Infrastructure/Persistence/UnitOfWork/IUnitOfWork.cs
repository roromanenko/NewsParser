using Npgsql;

namespace Infrastructure.Persistence.UnitOfWork;

internal interface IUnitOfWork
{
    NpgsqlConnection? CurrentConnection { get; }
    NpgsqlTransaction? CurrentTransaction { get; }
    Task BeginAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
