using Core.DomainModels;
using Core.Interfaces.Repositories;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class PublicationRepository(NewsParserDbContext db) : IPublicationRepository
{
	public async Task AddRangeAsync(Guid articleId, Guid editorId, List<Publication> publications, CancellationToken cancellationToken = default)
	{
		var entities = publications.Select(p => p.ToEntity(articleId, editorId)).ToList();
		await db.Publications.AddRangeAsync(entities, cancellationToken);
		await db.SaveChangesAsync(cancellationToken);
	}

	public async Task<List<Publication>> GetPendingForContentGenerationAsync(int batchSize, CancellationToken cancellationToken = default)
	{
		var entities = await db.Publications
			.Include(p => p.Article)
				.ThenInclude(a => a.RawArticle)
			.Include(p => p.PublishTarget)
			.Where(p => p.Status == PublicationStatus.Pending.ToString())
			.OrderBy(p => p.CreatedAt)
			.Take(batchSize)
			.ToListAsync(cancellationToken);

		return entities.Select(p => p.ToDomain()).ToList();
	}

	public async Task<List<Publication>> GetReadyForPublishAsync(int batchSize, CancellationToken cancellationToken = default)
	{
		var entities = await db.Publications
			.Include(p => p.PublishTarget)
			.Where(p => p.Status == PublicationStatus.ContentReady.ToString())
			.OrderBy(p => p.CreatedAt)
			.Take(batchSize)
			.ToListAsync(cancellationToken);

		return entities.Select(p => p.ToDomain()).ToList();
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
			.ExecuteUpdateAsync(s => s
				.SetProperty(p => p.PublishedAt, publishedAt),
			cancellationToken);
	}

	public async Task AddPublishLogAsync(
		PublishLog log,
		CancellationToken cancellationToken = default)
	{
		var entity = log.ToEntity();
		await db.PublishLogs.AddAsync(entity, cancellationToken);
		await db.SaveChangesAsync(cancellationToken);
	}

	public async Task<string?> GetExternalMessageIdAsync(
		Guid publicationId,
		CancellationToken cancellationToken = default)
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

	public async Task<Publication?> GetOriginalEventPublicationAsync(
	Guid eventId,
	CancellationToken cancellationToken = default)
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

	public async Task AddEventUpdatePublicationAsync(
		Publication publication,
		Guid articleId,
		CancellationToken cancellationToken = default)
	{
		var entity = publication.ToEntity(articleId, editorId: null);
		await db.Publications.AddAsync(entity, cancellationToken);
		await db.SaveChangesAsync(cancellationToken);
	}
}