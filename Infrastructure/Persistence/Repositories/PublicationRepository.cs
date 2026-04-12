using Core.DomainModels;
using Core.Interfaces.Repositories;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class PublicationRepository(NewsParserDbContext db) : IPublicationRepository
{
	public async Task<List<Publication>> GetPendingForGenerationAsync(int batchSize, CancellationToken cancellationToken = default)
	{
		var statusStr = PublicationStatus.Created.ToString();

		var lockedIds = await db.Publications
			.FromSql(
				$"SELECT * FROM publications WHERE \"Status\" = {statusStr} ORDER BY \"CreatedAt\" LIMIT {batchSize} FOR UPDATE SKIP LOCKED")
			.Select(p => p.Id)
			.ToListAsync(cancellationToken);

		if (lockedIds.Count == 0)
			return [];

		var entities = await db.Publications
			.Where(p => lockedIds.Contains(p.Id))
			.Include(p => p.Article)
			.Include(p => p.PublishTarget)
			.Include(p => p.Event)
				.ThenInclude(e => e!.Articles)
			.AsNoTracking()
			.ToListAsync(cancellationToken);

		return entities.Select(p => p.ToDomain()).ToList();
	}

	public async Task<List<Publication>> GetPendingForPublishAsync(int batchSize, CancellationToken cancellationToken = default)
	{
		var statusStr = PublicationStatus.Approved.ToString();

		var lockedIds = await db.Publications
			.FromSql(
				$"SELECT * FROM publications WHERE \"Status\" = {statusStr} ORDER BY \"CreatedAt\" LIMIT {batchSize} FOR UPDATE SKIP LOCKED")
			.Select(p => p.Id)
			.ToListAsync(cancellationToken);

		if (lockedIds.Count == 0)
			return [];

		var entities = await db.Publications
			.Where(p => lockedIds.Contains(p.Id))
			.Include(p => p.PublishTarget)
			.Include(p => p.Article)
			.AsNoTracking()
			.ToListAsync(cancellationToken);

		return entities.Select(p => p.ToDomain()).ToList();
	}

	public async Task AddAsync(Publication publication, CancellationToken cancellationToken = default)
	{
		var entity = publication.ToEntity(publication.Article.Id);
		await db.Publications.AddAsync(entity, cancellationToken);
		await db.SaveChangesAsync(cancellationToken);
	}

	public async Task<Publication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var entity = await db.Publications
			.Include(p => p.PublishTarget)
			.AsNoTracking()
			.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

		return entity?.ToDomain();
	}

	public async Task<Publication?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var entity = await db.Publications
			.Include(p => p.PublishTarget)
			.Include(p => p.PublishLogs)
			.Include(p => p.Event)
				.ThenInclude(e => e!.Articles)
					.ThenInclude(a => a.MediaFiles)
			.AsNoTracking()
			.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

		return entity?.ToDomain();
	}

	public async Task<List<Publication>> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default)
	{
		var entities = await db.Publications
			.Where(p => p.EventId == eventId)
			.Include(p => p.PublishTarget)
			.OrderBy(p => p.CreatedAt)
			.AsNoTracking()
			.ToListAsync(cancellationToken);

		return entities.Select(p => p.ToDomain()).ToList();
	}

	public async Task<List<Publication>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default)
	{
		var entities = await db.Publications
			.Include(p => p.PublishTarget)
			.Include(p => p.Event)
			.OrderByDescending(p => p.CreatedAt)
			.Skip((page - 1) * pageSize)
			.Take(pageSize)
			.AsNoTracking()
			.ToListAsync(cancellationToken);

		return entities.Select(p => p.ToDomain()).ToList();
	}

	public async Task<int> CountAllAsync(CancellationToken cancellationToken = default)
	{
		return await db.Publications.CountAsync(cancellationToken);
	}

	public async Task UpdateStatusAsync(Guid id, PublicationStatus status, CancellationToken cancellationToken = default)
	{
		await db.Publications
			.Where(p => p.Id == id)
			.ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status.ToString()), cancellationToken);
	}

	public async Task UpdateGeneratedContentAsync(Guid id, string content, CancellationToken cancellationToken = default)
	{
		await db.Publications
			.Where(p => p.Id == id)
			.ExecuteUpdateAsync(s => s.SetProperty(p => p.GeneratedContent, content), cancellationToken);
	}

	public async Task UpdatePublishedAtAsync(Guid id, DateTimeOffset publishedAt, CancellationToken cancellationToken = default)
	{
		await db.Publications
			.Where(p => p.Id == id)
			.ExecuteUpdateAsync(s => s.SetProperty(p => p.PublishedAt, publishedAt), cancellationToken);
	}

	public async Task UpdateContentAndMediaAsync(Guid id, string content, List<Guid> mediaFileIds, CancellationToken cancellationToken = default)
	{
		await db.Publications
			.Where(p => p.Id == id)
			.ExecuteUpdateAsync(s => s
				.SetProperty(p => p.GeneratedContent, content)
				.SetProperty(p => p.SelectedMediaFileIds, mediaFileIds),
			cancellationToken);
	}

	public async Task UpdateApprovalAsync(Guid id, Guid editorId, DateTimeOffset approvedAt, CancellationToken cancellationToken = default)
	{
		await db.Publications
			.Where(p => p.Id == id)
			.ExecuteUpdateAsync(s => s
				.SetProperty(p => p.Status, PublicationStatus.Approved.ToString())
				.SetProperty(p => p.ApprovedAt, approvedAt)
				.SetProperty(p => p.ReviewedByEditorId, editorId),
			cancellationToken);
	}

	public async Task UpdateRejectionAsync(Guid id, Guid editorId, string reason, DateTimeOffset rejectedAt, CancellationToken cancellationToken = default)
	{
		await db.Publications
			.Where(p => p.Id == id)
			.ExecuteUpdateAsync(s => s
				.SetProperty(p => p.Status, PublicationStatus.Rejected.ToString())
				.SetProperty(p => p.RejectedAt, rejectedAt)
				.SetProperty(p => p.RejectionReason, reason)
				.SetProperty(p => p.ReviewedByEditorId, editorId),
			cancellationToken);
	}

	public async Task AddPublishLogAsync(PublishLog log, CancellationToken cancellationToken = default)
	{
		var entity = log.ToEntity();
		await db.PublishLogs.AddAsync(entity, cancellationToken);
		await db.SaveChangesAsync(cancellationToken);
	}

	public async Task<string?> GetExternalMessageIdAsync(Guid publicationId, CancellationToken cancellationToken = default)
	{
		return await db.PublishLogs
			.Where(l =>
				l.PublicationId == publicationId &&
				l.Status == PublishLogStatus.Success.ToString() &&
				l.ExternalMessageId != null)
			.OrderByDescending(l => l.AttemptedAt)
			.Select(l => l.ExternalMessageId)
			.FirstOrDefaultAsync(cancellationToken);
	}

	public async Task<Publication?> GetOriginalEventPublicationAsync(Guid eventId, CancellationToken cancellationToken = default)
	{
		var entity = await db.Publications
			.Include(p => p.PublishTarget)
			.Where(p =>
				p.EventId == eventId &&
				p.ParentPublicationId == null &&
				p.Status == PublicationStatus.Published.ToString())
			.OrderBy(p => p.CreatedAt)
			.FirstOrDefaultAsync(cancellationToken);

		return entity?.ToDomain();
	}

	public async Task AddEventUpdatePublicationAsync(Publication publication, Guid articleId, CancellationToken cancellationToken = default)
	{
		var entity = publication.ToEntity(articleId, editorId: null);
		await db.Publications.AddAsync(entity, cancellationToken);
		await db.SaveChangesAsync(cancellationToken);
	}
}
