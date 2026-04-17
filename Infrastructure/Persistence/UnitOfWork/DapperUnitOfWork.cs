using Infrastructure.Persistence.Connection;
using Npgsql;

namespace Infrastructure.Persistence.UnitOfWork;

internal sealed class DapperUnitOfWork(IDbConnectionFactory factory) : IUnitOfWork, IAsyncDisposable, IDisposable
{
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;

    public NpgsqlConnection? CurrentConnection => _connection;
    public NpgsqlTransaction? CurrentTransaction => _transaction;

    public async Task BeginAsync(CancellationToken ct = default)
    {
        _connection = await factory.CreateOpenAsync(ct);
        _transaction = await _connection.BeginTransactionAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction to commit.");

        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            return;

        await _transaction.RollbackAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
