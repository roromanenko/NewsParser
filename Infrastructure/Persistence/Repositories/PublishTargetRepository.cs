using Core.DomainModels;
using Core.Interfaces.Repositories;
using Dapper;
using Infrastructure.Persistence.Connection;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Mappers;
using Infrastructure.Persistence.Repositories.Sql;
using Infrastructure.Persistence.UnitOfWork;

namespace Infrastructure.Persistence.Repositories;

internal class PublishTargetRepository(IDbConnectionFactory factory, IUnitOfWork uow) : IPublishTargetRepository
{
    public async Task<List<PublishTarget>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entities = await conn.QueryAsync<PublishTargetEntity>(
            new CommandDefinition(PublishTargetSql.GetAll, cancellationToken: cancellationToken));
        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<List<PublishTarget>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entities = await conn.QueryAsync<PublishTargetEntity>(
            new CommandDefinition(PublishTargetSql.GetActive, cancellationToken: cancellationToken));
        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<PublishTarget?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entity = await conn.QuerySingleOrDefaultAsync<PublishTargetEntity>(
            new CommandDefinition(PublishTargetSql.GetById, new { id }, cancellationToken: cancellationToken));
        return entity?.ToDomain();
    }

    public async Task<PublishTarget> CreateAsync(PublishTarget target, CancellationToken cancellationToken = default)
    {
        var entity = target.ToEntity();
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(PublishTargetSql.Insert, new
        {
            entity.Id,
            entity.Name,
            entity.Platform,
            entity.Identifier,
            entity.SystemPrompt,
            entity.IsActive,
        }, cancellationToken: cancellationToken));
        return entity.ToDomain();
    }

    public async Task UpdateAsync(PublishTarget target, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(PublishTargetSql.Update, new
        {
            target.Id,
            target.Name,
            target.Identifier,
            target.SystemPrompt,
            target.IsActive,
        }, cancellationToken: cancellationToken));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(PublishTargetSql.Delete,
            new { id },
            cancellationToken: cancellationToken));
    }
}
