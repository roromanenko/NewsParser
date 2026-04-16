using Core.DomainModels;
using Core.Interfaces.Repositories;
using Dapper;
using Infrastructure.Persistence.Connection;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Mappers;
using Infrastructure.Persistence.Repositories.Sql;
using Infrastructure.Persistence.UnitOfWork;

namespace Infrastructure.Persistence.Repositories;

internal class MediaFileRepository(IDbConnectionFactory factory, IUnitOfWork uow) : IMediaFileRepository
{
    public async Task AddAsync(MediaFile mediaFile, CancellationToken cancellationToken = default)
    {
        var entity = mediaFile.ToEntity();
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(MediaFileSql.Insert, new
        {
            entity.Id,
            entity.ArticleId,
            entity.R2Key,
            entity.OriginalUrl,
            entity.ContentType,
            entity.SizeBytes,
            entity.Kind,
            entity.CreatedAt,
        }, cancellationToken: cancellationToken));
    }

    public async Task<List<MediaFile>> GetByArticleIdAsync(Guid articleId, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entities = await conn.QueryAsync<MediaFileEntity>(
            new CommandDefinition(MediaFileSql.GetByArticleId, new { articleId }, cancellationToken: cancellationToken));
        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<bool> ExistsByArticleAndUrlAsync(Guid articleId, string originalUrl, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(MediaFileSql.ExistsByArticleAndUrl,
                new { articleId, originalUrl },
                cancellationToken: cancellationToken));
    }

    public async Task<List<MediaFile>> GetByIdsAsync(List<Guid> ids, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entities = await conn.QueryAsync<MediaFileEntity>(
            new CommandDefinition(MediaFileSql.GetByIds, new { ids = ids.ToArray() }, cancellationToken: cancellationToken));
        return entities.Select(e => e.ToDomain()).ToList();
    }
}
