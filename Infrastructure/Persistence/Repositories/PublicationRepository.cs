using Core.DomainModels;
using Core.Interfaces.Repositories;
using Dapper;
using Infrastructure.Persistence.Connection;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Mappers;
using Infrastructure.Persistence.Repositories.Sql;

namespace Infrastructure.Persistence.Repositories;

internal class PublicationRepository(IDbConnectionFactory factory) : IPublicationRepository
{
    public async Task<List<Publication>> GetPendingForGenerationAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);

        var rows = new List<(PublicationEntity, ArticleEntity, PublishTargetEntity, EventEntity?)>();
        await conn.QueryAsync<PublicationEntity, ArticleEntity, PublishTargetEntity, EventEntity?, PublicationEntity>(
            new CommandDefinition(PublicationSql.GetPendingForGeneration, new { batchSize }, cancellationToken: cancellationToken),
            (p, a, t, e) => { rows.Add((p, a, t, e)); return p; },
            splitOn: "Id,Id,Id");

        if (rows.Count == 0) return [];

        var eventIds = rows.Select(r => r.Item4?.Id).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        Dictionary<Guid, List<ArticleEntity>> eventArticles = [];

        if (eventIds.Length > 0)
        {
            var articles = await conn.QueryAsync<ArticleEntity>(
                new CommandDefinition(PublicationSql.GetEventArticlesByEventIds, new { eventIds }, cancellationToken: cancellationToken));
            eventArticles = articles.GroupBy(a => a.EventId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        }

        return rows.Select(r =>
        {
            var (pub, article, target, evt) = r;
            if (evt is not null && eventArticles.TryGetValue(evt.Id, out var evtArticles))
                evt.Articles = evtArticles;
            return BuildPublication(pub, article, target, evt);
        }).ToList();
    }

    public async Task<List<Publication>> GetPendingForPublishAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);

        var rows = new List<(PublicationEntity, PublishTargetEntity, ArticleEntity)>();
        await conn.QueryAsync<PublicationEntity, PublishTargetEntity, ArticleEntity, PublicationEntity>(
            new CommandDefinition(PublicationSql.GetPendingForPublish, new { batchSize }, cancellationToken: cancellationToken),
            (p, t, a) => { rows.Add((p, t, a)); return p; },
            splitOn: "Id,Id");

        return rows.Select(r =>
        {
            var (pub, target, article) = r;
            return BuildPublication(pub, article, target, evt: null);
        }).ToList();
    }

    public async Task AddAsync(Publication publication, CancellationToken cancellationToken = default)
    {
        var entity = publication.ToEntity(publication.Article.Id);
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(PublicationSql.Insert,
            BuildInsertParameters(entity),
            cancellationToken: cancellationToken));
    }

    public async Task<Publication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        PublicationEntity? pub = null;
        PublishTargetEntity? target = null;

        await conn.QueryAsync<PublicationEntity, PublishTargetEntity, PublicationEntity>(
            new CommandDefinition(PublicationSql.GetById, new { id }, cancellationToken: cancellationToken),
            (p, t) => { pub = p; target = t; return p; },
            splitOn: "Id");

        if (pub is null) return null;

        pub.PublishTarget = target ?? new PublishTargetEntity();
        return BuildPublicationWithoutArticle(pub);
    }

    public async Task<Publication?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);

        PublicationEntity? pub = null;
        PublishTargetEntity? target = null;

        await conn.QueryAsync<PublicationEntity, PublishTargetEntity, PublicationEntity>(
            new CommandDefinition(PublicationSql.GetDetailPublicationWithTarget, new { id }, cancellationToken: cancellationToken),
            (p, t) => { pub = p; target = t; return p; },
            splitOn: "Id");

        if (pub is null) return null;

        pub.PublishTarget = target ?? new PublishTargetEntity();

        var logs = await conn.QueryAsync<PublishLogEntity>(
            new CommandDefinition(PublicationSql.GetDetailPublishLogs, new { id }, cancellationToken: cancellationToken));

        EventEntity? evt = null;
        if (pub.EventId.HasValue)
        {
            evt = await conn.QuerySingleOrDefaultAsync<EventEntity>(
                new CommandDefinition(PublicationSql.GetDetailEvent, new { eventId = pub.EventId.Value }, cancellationToken: cancellationToken));

            if (evt is not null)
            {
                var evtArticles = (await conn.QueryAsync<ArticleEntity>(
                    new CommandDefinition(PublicationSql.GetDetailEventArticles,
                        new { eventId = pub.EventId.Value }, cancellationToken: cancellationToken))).ToList();

                if (evtArticles.Count > 0)
                {
                    var articleIds = evtArticles.Select(a => a.Id).ToArray();
                    var media = await conn.QueryAsync<MediaFileEntity>(
                        new CommandDefinition(PublicationSql.GetDetailMediaFiles,
                            new { articleIds }, cancellationToken: cancellationToken));

                    var mediaByArticle = media.GroupBy(m => m.ArticleId)
                        .ToDictionary(g => g.Key, g => g.ToList());

                    foreach (var art in evtArticles)
                        art.MediaFiles = mediaByArticle.TryGetValue(art.Id, out var files) ? files : [];
                }

                evt.Articles = evtArticles;
            }
        }

        return BuildPublicationDetail(pub, logs.ToList(), evt);
    }

    public async Task<List<Publication>> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var rows = new List<(PublicationEntity pub, PublishTargetEntity target)>();

        await conn.QueryAsync<PublicationEntity, PublishTargetEntity, PublicationEntity>(
            new CommandDefinition(PublicationSql.GetByEventId, new { eventId }, cancellationToken: cancellationToken),
            (p, t) => { rows.Add((p, t)); return p; },
            splitOn: "Id");

        return rows.Select(r =>
        {
            r.pub.PublishTarget = r.target;
            return BuildPublicationWithoutArticle(r.pub);
        }).ToList();
    }

    public async Task<List<Publication>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var offset = (page - 1) * pageSize;
        await using var conn = await factory.CreateOpenAsync(cancellationToken);

        var rows = new List<(PublicationEntity pub, PublishTargetEntity target, EventEntity? evt)>();

        await conn.QueryAsync<PublicationEntity, PublishTargetEntity, EventEntity?, PublicationEntity>(
            new CommandDefinition(PublicationSql.GetAll, new { pageSize, offset }, cancellationToken: cancellationToken),
            (p, t, e) => { rows.Add((p, t, e)); return p; },
            splitOn: "Id,Id");

        return rows.Select(r =>
        {
            r.pub.PublishTarget = r.target;
            r.pub.Event = r.evt;
            return BuildPublicationWithoutArticle(r.pub);
        }).ToList();
    }

    public async Task<int> CountAllAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(PublicationSql.CountAll, cancellationToken: cancellationToken));
    }

    public async Task UpdateStatusAsync(Guid id, PublicationStatus status, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(PublicationSql.UpdateStatus,
            new { id, status = status.ToString() },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateGeneratedContentAsync(Guid id, string content, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(PublicationSql.UpdateGeneratedContent,
            new { id, content },
            cancellationToken: cancellationToken));
    }

    public async Task UpdatePublishedAtAsync(Guid id, DateTimeOffset publishedAt, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(PublicationSql.UpdatePublishedAt,
            new { id, publishedAt },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateContentAndMediaAsync(Guid id, string content, List<Guid> mediaFileIds, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(PublicationSql.UpdateContentAndMedia,
            new { id, content, selectedMediaFileIds = mediaFileIds },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateApprovalAsync(Guid id, Guid editorId, DateTimeOffset approvedAt, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(PublicationSql.UpdateApproval,
            new { id, editorId, approvedAt },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateRejectionAsync(Guid id, Guid editorId, string reason, DateTimeOffset rejectedAt, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(PublicationSql.UpdateRejection,
            new { id, editorId, reason, rejectedAt },
            cancellationToken: cancellationToken));
    }

    public async Task AddPublishLogAsync(PublishLog log, CancellationToken cancellationToken = default)
    {
        var entity = log.ToEntity();
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(PublicationSql.InsertPublishLog, new
        {
            entity.Id,
            entity.PublicationId,
            entity.Status,
            entity.ErrorMessage,
            entity.AttemptedAt,
            entity.ExternalMessageId,
        }, cancellationToken: cancellationToken));
    }

    public async Task<string?> GetExternalMessageIdAsync(Guid publicationId, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        return await conn.ExecuteScalarAsync<string?>(
            new CommandDefinition(PublicationSql.GetExternalMessageId,
                new { publicationId },
                cancellationToken: cancellationToken));
    }

    public async Task<Publication?> GetOriginalEventPublicationAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        PublicationEntity? pub = null;
        PublishTargetEntity? target = null;

        await conn.QueryAsync<PublicationEntity, PublishTargetEntity, PublicationEntity>(
            new CommandDefinition(PublicationSql.GetOriginalEventPublication, new { eventId }, cancellationToken: cancellationToken),
            (p, t) => { pub = p; target = t; return p; },
            splitOn: "Id");

        if (pub is null) return null;
        pub.PublishTarget = target ?? new PublishTargetEntity();
        return BuildPublicationWithoutArticle(pub);
    }

    public async Task AddEventUpdatePublicationAsync(Publication publication, Guid articleId, CancellationToken cancellationToken = default)
    {
        var entity = publication.ToEntity(articleId, editorId: null);
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(PublicationSql.Insert,
            BuildInsertParameters(entity),
            cancellationToken: cancellationToken));
    }

    private static Publication BuildPublication(
        PublicationEntity pub, ArticleEntity article, PublishTargetEntity target, EventEntity? evt)
    {
        return new Publication
        {
            Id = pub.Id,
            Article = article.ToDomain(),
            PublishTargetId = pub.PublishTargetId,
            PublishTarget = target.ToDomain(),
            GeneratedContent = pub.GeneratedContent,
            Status = Enum.Parse<PublicationStatus>(pub.Status),
            CreatedAt = pub.CreatedAt,
            PublishedAt = pub.PublishedAt,
            ApprovedAt = pub.ApprovedAt,
            EventId = pub.EventId,
            Event = evt?.ToDomain(),
            ParentPublicationId = pub.ParentPublicationId,
            UpdateContext = pub.UpdateContext,
            SelectedMediaFileIds = pub.SelectedMediaFileIds ?? [],
            ReviewedByEditorId = pub.ReviewedByEditorId,
            RejectedAt = pub.RejectedAt,
            RejectionReason = pub.RejectionReason,
        };
    }

    private static Publication BuildPublicationWithoutArticle(PublicationEntity pub) =>
        new()
        {
            Id = pub.Id,
            Article = pub.Article?.ToDomain() ?? new Article(),
            PublishTargetId = pub.PublishTargetId,
            PublishTarget = pub.PublishTarget?.ToDomain() ?? new PublishTarget(),
            GeneratedContent = pub.GeneratedContent,
            Status = Enum.Parse<PublicationStatus>(pub.Status),
            CreatedAt = pub.CreatedAt,
            PublishedAt = pub.PublishedAt,
            ApprovedAt = pub.ApprovedAt,
            EventId = pub.EventId,
            Event = pub.Event?.ToDomain(),
            ParentPublicationId = pub.ParentPublicationId,
            UpdateContext = pub.UpdateContext,
            SelectedMediaFileIds = pub.SelectedMediaFileIds ?? [],
            ReviewedByEditorId = pub.ReviewedByEditorId,
            RejectedAt = pub.RejectedAt,
            RejectionReason = pub.RejectionReason,
        };

    private static Publication BuildPublicationDetail(
        PublicationEntity pub, List<PublishLogEntity> logs, EventEntity? evt)
    {
        var publication = BuildPublicationWithoutArticle(pub);
        publication.PublishLogs = logs.Select(l => l.ToDomain()).ToList();
        publication.Event = evt?.ToDomain();
        return publication;
    }

    private static DynamicParameters BuildInsertParameters(PublicationEntity entity)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", entity.Id);
        parameters.Add("ArticleId", entity.ArticleId);
        parameters.Add("EditorId", entity.EditorId);
        parameters.Add("PublishTargetId", entity.PublishTargetId);
        parameters.Add("GeneratedContent", entity.GeneratedContent);
        parameters.Add("Status", entity.Status);
        parameters.Add("CreatedAt", entity.CreatedAt);
        parameters.Add("PublishedAt", entity.PublishedAt);
        parameters.Add("ApprovedAt", entity.ApprovedAt);
        parameters.Add("EventId", entity.EventId);
        parameters.Add("ParentPublicationId", entity.ParentPublicationId);
        parameters.Add("UpdateContext", entity.UpdateContext);
        parameters.Add("SelectedMediaFileIds", entity.SelectedMediaFileIds);
        parameters.Add("ReviewedByEditorId", entity.ReviewedByEditorId);
        parameters.Add("RejectedAt", entity.RejectedAt);
        parameters.Add("RejectionReason", entity.RejectionReason);
        return parameters;
    }
}
