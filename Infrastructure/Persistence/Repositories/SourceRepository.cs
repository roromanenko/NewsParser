using Core.DomainModels;
using Core.Interfaces.Repositories;
using Dapper;
using Infrastructure.Persistence.Connection;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Mappers;
using Infrastructure.Persistence.Repositories.Sql;
using Infrastructure.Persistence.UnitOfWork;

namespace Infrastructure.Persistence.Repositories;

internal class SourceRepository(IDbConnectionFactory factory, IUnitOfWork uow) : ISourceRepository
{
    public async Task<List<Source>> GetActiveAsync(SourceType type, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entities = await conn.QueryAsync<SourceEntity>(
            new CommandDefinition(SourceSql.GetActive, new { type = type.ToString() }, cancellationToken: cancellationToken));
        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task UpdateLastFetchedAtAsync(Guid sourceId, DateTimeOffset fetchedAt, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(SourceSql.UpdateLastFetchedAt,
            new { sourceId, fetchedAt },
            cancellationToken: cancellationToken));
    }

    public async Task<List<Source>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entities = await conn.QueryAsync<SourceEntity>(
            new CommandDefinition(SourceSql.GetAll, cancellationToken: cancellationToken));
        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<List<Source>> GetAllByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entities = await conn.QueryAsync<SourceEntity>(
            new CommandDefinition(SourceSql.GetAllByProject, new { projectId }, cancellationToken: cancellationToken));
        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<Source?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entity = await conn.QuerySingleOrDefaultAsync<SourceEntity>(
            new CommandDefinition(SourceSql.GetById, new { id }, cancellationToken: cancellationToken));
        return entity?.ToDomain();
    }

    public async Task<bool> ExistsByUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(SourceSql.ExistsByUrl, new { url }, cancellationToken: cancellationToken));
    }

    public async Task<bool> ExistsByProjectAndUrlAsync(Guid projectId, string url, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(SourceSql.ExistsByProjectAndUrl, new { projectId, url }, cancellationToken: cancellationToken));
    }

    public async Task<Source> CreateAsync(Source source, CancellationToken cancellationToken = default)
    {
        var entity = source.ToEntity();
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(SourceSql.Insert, new
        {
            entity.Id,
            entity.Name,
            entity.Url,
            entity.Type,
            entity.ProjectId,
            entity.IsActive,
            entity.LastFetchedAt,
        }, cancellationToken: cancellationToken));
        return entity.ToDomain();
    }

    public async Task UpdateAsync(Source source, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(SourceSql.UpdateFields, new
        {
            source.Id,
            source.Name,
            source.Url,
            source.IsActive,
        }, cancellationToken: cancellationToken));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(SourceSql.Delete,
            new { id },
            cancellationToken: cancellationToken));
    }
}
