using Core.DomainModels;
using Core.Interfaces.Repositories;
using Dapper;
using Infrastructure.Configuration;
using Infrastructure.Persistence.Connection;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Mappers;
using Infrastructure.Persistence.Repositories.Sql;
using Infrastructure.Persistence.UnitOfWork;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;

namespace Infrastructure.Persistence.Repositories;

internal class EventRepository(
    IDbConnectionFactory factory,
    IUnitOfWork uow,
    IOptions<EventImportanceOptions> importanceOptions) : IEventRepository
{
    private readonly double _halfLifeHours = importanceOptions.Value.HalfLifeHours;

    public async Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        return await FetchEventWithRelationsAsync(conn, id, includeMedia: false, cancellationToken);
    }

    public async Task<List<Event>> GetActiveEventsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var events = await conn.QueryAsync<EventEntity>(
            new CommandDefinition(EventSql.GetActiveEvents, new { projectId }, cancellationToken: cancellationToken));

        var eventList = events.ToList();
        if (eventList.Count == 0) return [];

        var ids = eventList.Select(e => e.Id).ToArray();
        var articles = await FetchArticlesByEventIdsAsync(conn, ids, cancellationToken);

        foreach (var evt in eventList)
            evt.Articles = articles.Where(a => a.EventId == evt.Id).ToList();

        return eventList.Select(e => e.ToDomain()).ToList();
    }

    public async Task<List<(Event Event, double Similarity)>> FindSimilarEventsAsync(
        Guid projectId, float[] embedding, double threshold, int windowHours, int maxTake,
        CancellationToken cancellationToken = default)
    {
        var vector = new Vector(embedding);
        var windowStart = DateTimeOffset.UtcNow.AddHours(-windowHours);

        await using var conn = await factory.CreateOpenAsync(cancellationToken);

        var rows = await conn.QueryAsync<EventWithSimilarityRow>(
            new CommandDefinition(EventSql.FindSimilarEvents,
                new { projectId, vector, windowStart, threshold, maxTake },
                cancellationToken: cancellationToken));

        return rows.Select(r => (r.ToDomain(), r.Similarity)).ToList();
    }

    public async Task<Event> CreateAsync(Event evt, CancellationToken cancellationToken = default)
    {
        var entity = evt.ToEntity();
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(EventSql.Insert, new
        {
            entity.Id,
            entity.Title,
            entity.Summary,
            entity.Status,
            entity.FirstSeenAt,
            entity.LastUpdatedAt,
            Embedding = entity.Embedding,
            entity.ArticleCount,
            entity.ProjectId,
        }, cancellationToken: cancellationToken));
        return entity.ToDomain();
    }

    public async Task UpdateSummaryTitleAndEmbeddingAsync(
        Guid id, string title, string summary, float[] embedding,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(EventSql.UpdateSummaryTitleAndEmbedding, new
        {
            id,
            title,
            summary,
            embedding = new Vector(embedding),
            lastUpdatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken: cancellationToken));
    }

    public async Task UpdateLastUpdatedAtAsync(Guid id, DateTimeOffset lastUpdatedAt, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(EventSql.UpdateLastUpdatedAt,
            new { id, lastUpdatedAt },
            cancellationToken: cancellationToken));
    }

    public async Task AssignArticleToEventAsync(
        Guid articleId, Guid eventId, ArticleRole role,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(EventSql.AssignArticleToEvent, new
        {
            articleId,
            eventId,
            role = role.ToString(),
            addedToEventAt = DateTimeOffset.UtcNow,
        }, cancellationToken: cancellationToken));
    }

    public async Task AddEventUpdateAsync(EventUpdate eventUpdate, CancellationToken cancellationToken = default)
    {
        var entity = eventUpdate.ToEntity();
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(EventSql.InsertEventUpdate, new
        {
            entity.Id,
            entity.EventId,
            entity.ArticleId,
            entity.FactSummary,
            entity.IsPublished,
            entity.CreatedAt,
        }, cancellationToken: cancellationToken));
    }

    public async Task AddContradictionAsync(
        Contradiction contradiction, List<Guid> articleIds,
        CancellationToken cancellationToken = default)
    {
        var entity = contradiction.ToEntity();
        var conn = uow.CurrentConnection ?? await factory.CreateOpenAsync(cancellationToken);
        var ownedConn = uow.CurrentConnection is null;

        try
        {
            await conn.ExecuteAsync(new CommandDefinition(EventSql.InsertContradiction, new
            {
                entity.Id,
                entity.EventId,
                entity.Description,
                entity.IsResolved,
                entity.CreatedAt,
            }, transaction: uow.CurrentTransaction, cancellationToken: cancellationToken));

            foreach (var articleId in articleIds)
            {
                await conn.ExecuteAsync(new CommandDefinition(EventSql.InsertContradictionArticle,
                    new { ContradictionId = entity.Id, ArticleId = articleId },
                    transaction: uow.CurrentTransaction,
                    cancellationToken: cancellationToken));
            }
        }
        finally
        {
            if (ownedConn)
                await conn.DisposeAsync();
        }
    }

    public async Task<List<EventUpdate>> GetUnpublishedUpdatesAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);

        var updates = (await conn.QueryAsync<EventUpdateEntity>(
            new CommandDefinition(EventSql.GetUnpublishedUpdates, new { batchSize }, cancellationToken: cancellationToken))).ToList();

        if (updates.Count == 0) return [];

        var eventIds = updates.Select(u => u.EventId).Distinct().ToArray();
        var articleIds = updates.Select(u => u.ArticleId).Distinct().ToArray();

        var events = await conn.QueryAsync<EventEntity>(
            new CommandDefinition(EventSql.GetUnpublishedUpdateEvents, new { eventIds }, cancellationToken: cancellationToken));
        var articles = await conn.QueryAsync<ArticleEntity>(
            new CommandDefinition(EventSql.GetUnpublishedUpdateArticles, new { articleIds }, cancellationToken: cancellationToken));

        var eventMap = events.ToDictionary(e => e.Id);
        var articleMap = articles.ToDictionary(a => a.Id);

        return updates.Select(u => new EventUpdate
        {
            Id = u.Id,
            EventId = u.EventId,
            ArticleId = u.ArticleId,
            FactSummary = u.FactSummary,
            IsPublished = u.IsPublished,
            CreatedAt = u.CreatedAt,
            Event = eventMap.TryGetValue(u.EventId, out var evt) ? evt.ToDomain() : new Event(),
            Article = articleMap.TryGetValue(u.ArticleId, out var art) ? art.ToDomain() : new Article(),
        }).ToList();
    }

    public async Task MarkUpdatePublishedAsync(Guid eventUpdateId, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(EventSql.MarkUpdatePublished,
            new { eventUpdateId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> CountUpdatesFromAsync(Guid eventId, DateTimeOffset from, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(EventSql.CountUpdatesFrom, new { eventId, from }, cancellationToken: cancellationToken));
    }

    public async Task<DateTimeOffset?> GetLastUpdateTimeAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var dt = await conn.ExecuteScalarAsync<DateTime?>(
            new CommandDefinition(EventSql.GetLastUpdateTime, new { eventId }, cancellationToken: cancellationToken));
        return dt.HasValue ? new DateTimeOffset(dt.Value, TimeSpan.Zero) : null;
    }

    public async Task<List<Event>> GetPagedAsync(
        Guid projectId, int page, int pageSize, string? search, string sortBy, ImportanceTier? tier,
        CancellationToken cancellationToken = default)
    {
        var offset = (page - 1) * pageSize;
        var tierClause = tier.HasValue ? EventSql.TierFilterPaged : string.Empty;
        var orderByClause = BuildOrderByClause(sortBy);

        await using var conn = await factory.CreateOpenAsync(cancellationToken);

        List<EventEntity> eventEntities;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var escaped = QueryHelpers.EscapeILikePattern(search);
            var pattern = $"%{escaped}%";
            var sql = string.Format(EventSql.GetPagedWithSearch, orderByClause, tierClause);
            var parameters = BuildPagedParameters(projectId, pageSize, offset, tier, pattern: pattern, sortBy: sortBy);
            eventEntities = (await conn.QueryAsync<EventEntity>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).ToList();
        }
        else
        {
            var sql = string.Format(EventSql.GetPagedWithoutSearch, orderByClause, tierClause);
            var parameters = BuildPagedParameters(projectId, pageSize, offset, tier, sortBy: sortBy);
            eventEntities = (await conn.QueryAsync<EventEntity>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).ToList();
        }

        if (eventEntities.Count == 0) return [];

        var ids = eventEntities.Select(e => e.Id).ToArray();
        var articles = await FetchArticlesByEventIdsAsync(conn, ids, cancellationToken);
        var contradictions = await FetchContradictionsByEventIdsAsync(conn, ids, cancellationToken);

        foreach (var evt in eventEntities)
        {
            evt.Articles = articles.Where(a => a.EventId == evt.Id).ToList();
            evt.Contradictions = contradictions.Where(c => c.EventId == evt.Id).ToList();
        }

        return eventEntities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<int> CountAsync(Guid projectId, string? search, ImportanceTier? tier, CancellationToken cancellationToken = default)
    {
        var tierClause = tier.HasValue ? EventSql.TierFilterCount : string.Empty;

        await using var conn = await factory.CreateOpenAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var escaped = QueryHelpers.EscapeILikePattern(search);
            var pattern = $"%{escaped}%";
            var sql = string.Format(EventSql.CountWithSearch, tierClause);
            return await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(sql,
                    tier.HasValue
                        ? new { projectId, pattern, tier = tier.Value.ToString() }
                        : (object)new { projectId, pattern },
                    cancellationToken: cancellationToken));
        }

        var countSql = string.Format(EventSql.CountWithoutSearch, tierClause);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql,
                tier.HasValue
                    ? new { projectId, tier = tier.Value.ToString() }
                    : (object)new { projectId },
                cancellationToken: cancellationToken));
    }

    public async Task<Event?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        return await FetchEventWithRelationsAsync(conn, id, includeMedia: true, cancellationToken);
    }

    public async Task<Event?> GetWithContextAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var eventEntity = await conn.QuerySingleOrDefaultAsync<EventEntity>(
            new CommandDefinition(EventSql.GetById, new { id }, cancellationToken: cancellationToken));

        if (eventEntity is null) return null;

        eventEntity.Articles = (await conn.QueryAsync<ArticleEntity>(
            new CommandDefinition(EventSql.GetArticlesByEventId, new { id }, cancellationToken: cancellationToken))).ToList();

        eventEntity.EventUpdates = (await conn.QueryAsync<EventUpdateEntity>(
            new CommandDefinition(EventSql.GetEventUpdatesByEventId, new { id }, cancellationToken: cancellationToken))).ToList();

        eventEntity.Contradictions = [];

        return eventEntity.ToDomain();
    }

    public async Task ResolveContradictionAsync(Guid contradictionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(EventSql.ResolveContradiction,
            new { contradictionId },
            cancellationToken: cancellationToken));
    }

    public async Task MergeAsync(Guid sourceEventId, Guid targetEventId, CancellationToken cancellationToken = default)
    {
        var conn = uow.CurrentConnection ?? await factory.CreateOpenAsync(cancellationToken);
        var ownedConn = uow.CurrentConnection is null;

        try
        {
            var txn = uow.CurrentTransaction;

            await conn.ExecuteAsync(new CommandDefinition(EventSql.MergeArticles,
                new { sourceEventId, targetEventId }, transaction: txn, cancellationToken: cancellationToken));

            await conn.ExecuteAsync(new CommandDefinition(EventSql.MergeEventUpdates,
                new { sourceEventId, targetEventId }, transaction: txn, cancellationToken: cancellationToken));

            await conn.ExecuteAsync(new CommandDefinition(EventSql.MergeContradictions,
                new { sourceEventId, targetEventId }, transaction: txn, cancellationToken: cancellationToken));

            await conn.ExecuteAsync(new CommandDefinition(EventSql.ArchiveEvent,
                new { sourceEventId }, transaction: txn, cancellationToken: cancellationToken));

            await conn.ExecuteAsync(new CommandDefinition(EventSql.TouchLastUpdatedAt,
                new { targetEventId, now = DateTimeOffset.UtcNow }, transaction: txn, cancellationToken: cancellationToken));
        }
        finally
        {
            if (ownedConn)
                await conn.DisposeAsync();
        }
    }

    public async Task UpdateArticleRoleAsync(Guid articleId, ArticleRole role, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(EventSql.UpdateArticleRole,
            new { articleId, role = role.ToString() },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateStatusAsync(Guid id, EventStatus status, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(EventSql.UpdateEventStatus,
            new { id, status = status.ToString() },
            cancellationToken: cancellationToken));
    }

    public async Task MarkAsReclassifiedAsync(Guid articleId, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(EventSql.MarkArticleReclassified,
            new { articleId },
            cancellationToken: cancellationToken));
    }

    public async Task<EventImportanceStats> GetImportanceStatsAsync(Guid eventId, CancellationToken ct = default)
    {
        await using var conn = await factory.CreateOpenAsync(ct);
        var row = await conn.QuerySingleAsync<ImportanceStatsRow>(
            new CommandDefinition(EventSql.GetImportanceStats, new { eventId }, cancellationToken: ct));
        return new EventImportanceStats(
            row.ArticleCount,
            row.DistinctSourceCount,
            row.ArticlesLastHour,
            row.LastArticleAt);
    }

    public async Task UpdateImportanceAsync(
        Guid eventId, ImportanceTier tier, double baseScore, DateTimeOffset calculatedAt,
        CancellationToken ct = default)
    {
        await using var conn = await factory.CreateOpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(EventSql.UpdateImportance,
            new { eventId, tier = tier.ToString(), baseScore, calculatedAt },
            cancellationToken: ct));
    }

    private string BuildOrderByClause(string sortBy) => sortBy switch
    {
        EventSql.SortKeys.Oldest => """ORDER BY e."LastUpdatedAt" ASC""",
        EventSql.SortKeys.Importance => EventSql.ImportanceOrderBy,
        _ => """ORDER BY e."LastUpdatedAt" DESC"""
    };

    private object BuildPagedParameters(
        Guid projectId, int pageSize, int offset, ImportanceTier? tier,
        string? pattern = null, string sortBy = EventSql.SortKeys.Newest)
    {
        var dp = new DynamicParameters();
        dp.Add("projectId", projectId);
        dp.Add("pageSize", pageSize);
        dp.Add("offset", offset);

        if (pattern is not null)
            dp.Add("pattern", pattern);

        if (tier.HasValue)
            dp.Add("tier", tier.Value.ToString());

        if (sortBy == EventSql.SortKeys.Importance)
            dp.Add("halfLifeHours", _halfLifeHours);

        return dp;
    }

    private static async Task<Event?> FetchEventWithRelationsAsync(
        NpgsqlConnection conn, Guid id, bool includeMedia,
        CancellationToken cancellationToken)
    {
        var eventEntity = await conn.QuerySingleOrDefaultAsync<EventEntity>(
            new CommandDefinition(EventSql.GetById, new { id }, cancellationToken: cancellationToken));

        if (eventEntity is null) return null;

        var articles = (await conn.QueryAsync<ArticleEntity>(
            new CommandDefinition(EventSql.GetArticlesByEventId, new { id }, cancellationToken: cancellationToken))).ToList();

        if (includeMedia && articles.Count > 0)
        {
            var articleIds = articles.Select(a => a.Id).ToArray();
            var mediaFiles = await conn.QueryAsync<MediaFileEntity>(
                new CommandDefinition(EventSql.GetMediaFilesByArticleIds, new { ids = articleIds }, cancellationToken: cancellationToken));

            var mediaByArticle = mediaFiles.GroupBy(m => m.ArticleId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var article in articles)
                article.MediaFiles = mediaByArticle.TryGetValue(article.Id, out var media) ? media : [];
        }

        eventEntity.Articles = articles;

        eventEntity.EventUpdates = (await conn.QueryAsync<EventUpdateEntity>(
            new CommandDefinition(EventSql.GetEventUpdatesByEventId, new { id }, cancellationToken: cancellationToken))).ToList();

        var contradictions = (await conn.QueryAsync<ContradictionEntity>(
            new CommandDefinition(EventSql.GetContradictionsByEventId, new { id }, cancellationToken: cancellationToken))).ToList();

        if (contradictions.Count > 0)
        {
            var contradictionIds = contradictions.Select(c => c.Id).ToArray();
            var caRows = await conn.QueryAsync<ContradictionArticleEntity>(
                new CommandDefinition(EventSql.GetContradictionArticlesByContradictionIds,
                    new { ids = contradictionIds }, cancellationToken: cancellationToken));

            var caByContradiction = caRows.GroupBy(ca => ca.ContradictionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var c in contradictions)
                c.ContradictionArticles = caByContradiction.TryGetValue(c.Id, out var cas) ? cas : [];
        }

        eventEntity.Contradictions = contradictions;

        return eventEntity.ToDomain();
    }

    private static async Task<List<ArticleEntity>> FetchArticlesByEventIdsAsync(
        NpgsqlConnection conn, Guid[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0) return [];

        return (await conn.QueryAsync<ArticleEntity>(
            new CommandDefinition(EventSql.GetArticlesByEventIds, new { ids }, cancellationToken: cancellationToken))).ToList();
    }

    private static async Task<List<ContradictionEntity>> FetchContradictionsByEventIdsAsync(
        NpgsqlConnection conn, Guid[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0) return [];

        return (await conn.QueryAsync<ContradictionEntity>(
            new CommandDefinition(EventSql.GetContradictionsByEventIds, new { ids }, cancellationToken: cancellationToken))).ToList();
    }
}

internal sealed class EventWithSimilarityRow : EventEntity
{
    public double Similarity { get; init; }
}

internal sealed class ImportanceStatsRow
{
    public int ArticleCount { get; init; }
    public int DistinctSourceCount { get; init; }
    public int ArticlesLastHour { get; init; }
    public DateTimeOffset? LastArticleAt { get; init; }
}
