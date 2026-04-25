using Core.DomainModels;
using Core.Interfaces.Repositories;
using Dapper;
using Infrastructure.Persistence.Connection;
using Infrastructure.Persistence.Mappers;
using Infrastructure.Persistence.Repositories.Sql;
using Infrastructure.Persistence.UnitOfWork;

namespace Infrastructure.Persistence.Repositories;

internal class AiRequestLogRepository(IDbConnectionFactory factory, IUnitOfWork uow) : IAiRequestLogRepository
{
    public async Task AddAsync(AiRequestLog entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var entity = entry.ToEntity();
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(
            AiRequestLogSql.Insert,
            entity,
            cancellationToken: cancellationToken));
    }
}
