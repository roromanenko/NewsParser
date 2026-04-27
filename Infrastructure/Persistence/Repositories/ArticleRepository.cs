using System.Data;
using Core.DomainModels;
using Core.Interfaces.Repositories;
using Dapper;
using Infrastructure.Persistence.Connection;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Mappers;
using Infrastructure.Persistence.Repositories.Sql;
using Pgvector;

namespace Infrastructure.Persistence.Repositories;

internal class ArticleRepository(IDbConnectionFactory factory) : IArticleRepository
{
    public async Task AddAsync(Article article, CancellationToken cancellationToken = default)
    {
        var entity = article.ToEntity();
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var parameters = BuildArticleInsertParameters(entity);
        await conn.ExecuteAsync(new CommandDefinition(ArticleSql.Insert, parameters, cancellationToken: cancellationToken));
    }

    public async Task<Article?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);

        var articleDict = new Dictionary<Guid, ArticleEntity>();

        await conn.QueryAsync<ArticleEntity, MediaFileEntity?, ArticleEntity>(
            new CommandDefinition(ArticleSql.GetById, new { id }, cancellationToken: cancellationToken),
            (article, media) =>
            {
                if (!articleDict.TryGetValue(article.Id, out var existing))
                {
                    existing = article;
                    existing.MediaFiles = [];
                    articleDict[article.Id] = existing;
                }

                if (media is not null)
                    existing.MediaFiles.Add(media);

                return existing;
            },
            splitOn: "Id");

        return articleDict.Values.FirstOrDefault()?.ToDomain();
    }

    public async Task<List<Article>> GetAnalysisDoneAsync(
        Guid projectId, int page, int pageSize, string? search, string sortBy,
        CancellationToken cancellationToken = default)
    {
        var direction = sortBy == "oldest" ? "ASC" : "DESC";
        var offset = (page - 1) * pageSize;

        await using var conn = await factory.CreateOpenAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var escaped = QueryHelpers.EscapeILikePattern(search);
            var pattern = $"%{escaped}%";
            var sql = string.Format(ArticleSql.GetAnalysisDoneWithSearch, direction);
            var entities = await conn.QueryAsync<ArticleEntity>(
                new CommandDefinition(sql, new { projectId, pattern, pageSize, offset }, cancellationToken: cancellationToken));
            return entities.Select(e => e.ToDomain()).ToList();
        }
        else
        {
            var sql = string.Format(ArticleSql.GetAnalysisDoneWithoutSearch, direction);
            var entities = await conn.QueryAsync<ArticleEntity>(
                new CommandDefinition(sql, new { projectId, pageSize, offset }, cancellationToken: cancellationToken));
            return entities.Select(e => e.ToDomain()).ToList();
        }
    }

    public async Task<int> CountAnalysisDoneAsync(Guid projectId, string? search, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var escaped = QueryHelpers.EscapeILikePattern(search);
            var pattern = $"%{escaped}%";
            return await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(ArticleSql.CountAnalysisDoneWithSearch, new { projectId, pattern }, cancellationToken: cancellationToken));
        }

        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(ArticleSql.CountAnalysisDoneWithoutSearch, new { projectId }, cancellationToken: cancellationToken));
    }

    public async Task UpdateStatusAsync(Guid id, ArticleStatus status, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(ArticleSql.UpdateStatus,
            new { id, status = status.ToString() },
            cancellationToken: cancellationToken));
    }

    public async Task RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(ArticleSql.Reject,
            new { id, reason },
            cancellationToken: cancellationToken));
    }

    public async Task IncrementRetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(ArticleSql.IncrementRetry,
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<List<Article>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entities = await conn.QueryAsync<ArticleEntity>(
            new CommandDefinition(ArticleSql.GetPending, new { batchSize }, cancellationToken: cancellationToken));
        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<List<Article>> GetPendingForClassificationAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entities = await conn.QueryAsync<ArticleEntity>(
            new CommandDefinition(ArticleSql.GetPendingForClassification, new { batchSize }, cancellationToken: cancellationToken));
        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task UpdateKeyFactsAsync(Guid id, List<string> keyFacts, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(ArticleSql.UpdateKeyFacts,
            new { id, keyFacts },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateAnalysisResultAsync(
        Guid id, string category, List<string> tags, string sentiment,
        string language, string summary, string modelVersion,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var parameters = BuildTagsParameters(id, category, tags, sentiment, language, summary, modelVersion);
        await conn.ExecuteAsync(new CommandDefinition(ArticleSql.UpdateAnalysisResult, parameters, cancellationToken: cancellationToken));
    }

    public async Task UpdateEmbeddingAsync(Guid id, float[] embedding, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(ArticleSql.UpdateEmbedding,
            new { id, embedding = new Vector(embedding) },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> ExistsAsync(Guid sourceId, string externalId, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(ArticleSql.ExistsBySourceAndExternal,
                new { sourceId, externalId },
                cancellationToken: cancellationToken));
    }

    public async Task<bool> ExistsByUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(ArticleSql.ExistsByUrl, new { url }, cancellationToken: cancellationToken));
    }

    public async Task<List<string>> GetRecentTitlesForDeduplicationAsync(int windowHours, CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-windowHours);
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var titles = await conn.QueryAsync<string>(
            new CommandDefinition(ArticleSql.GetRecentTitlesForDeduplication,
                new { since },
                cancellationToken: cancellationToken));
        return titles.ToList();
    }

    private static DynamicParameters BuildTagsParameters(
        Guid id, string category, List<string> tags, string sentiment,
        string language, string summary, string modelVersion)
    {
        var parameters = new DynamicParameters();
        parameters.Add("id", id);
        parameters.Add("category", category);
        parameters.Add("tags", tags.ToArray(), dbType: DbType.Object);
        parameters.Add("sentiment", sentiment);
        parameters.Add("language", language);
        parameters.Add("summary", summary);
        parameters.Add("modelVersion", modelVersion);

        return parameters;
    }

    private static DynamicParameters BuildArticleInsertParameters(ArticleEntity entity)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", entity.Id);
        parameters.Add("OriginalContent", entity.OriginalContent);
        parameters.Add("SourceId", entity.SourceId);
        parameters.Add("OriginalUrl", entity.OriginalUrl);
        parameters.Add("PublishedAt", entity.PublishedAt);
        parameters.Add("ExternalId", entity.ExternalId);
        parameters.Add("Embedding", entity.Embedding);
        parameters.Add("Title", entity.Title);
        parameters.Add("Tags", entity.Tags, dbType: DbType.Object);
        parameters.Add("Category", entity.Category);
        parameters.Add("Sentiment", entity.Sentiment);
        parameters.Add("ProcessedAt", entity.ProcessedAt);
        parameters.Add("Status", entity.Status);
        parameters.Add("ModelVersion", entity.ModelVersion);
        parameters.Add("Language", entity.Language);
        parameters.Add("Summary", entity.Summary);
        parameters.Add("KeyFacts", entity.KeyFacts);
        parameters.Add("RejectionReason", entity.RejectionReason);
        parameters.Add("RetryCount", entity.RetryCount);
        parameters.Add("EventId", entity.EventId);
        parameters.Add("Role", entity.Role);
        parameters.Add("WasReclassified", entity.WasReclassified);
        parameters.Add("AddedToEventAt", entity.AddedToEventAt);
        parameters.Add("ProjectId", entity.ProjectId);
        return parameters;
    }
}
