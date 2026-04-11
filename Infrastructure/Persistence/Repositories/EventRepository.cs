using Core.DomainModels;
using Core.Interfaces.Repositories;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class EventRepository : IEventRepository
{
	private readonly NewsParserDbContext _context;

	public EventRepository(NewsParserDbContext context)
	{
		_context = context;
	}

	public async Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var entity = await _context.Events
			.Include(e => e.Articles)
			.Include(e => e.EventUpdates)
			.Include(e => e.Contradictions)
				.ThenInclude(c => c.ContradictionArticles)
			.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

		return entity?.ToDomain();
	}

	public async Task<List<Event>> GetActiveEventsAsync(CancellationToken cancellationToken = default)
	{
		var entities = await _context.Events
			.Where(e => e.Status == EventStatus.Active.ToString())
			.Include(e => e.Articles)
			.OrderByDescending(e => e.LastUpdatedAt)
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public async Task<List<(Event Event, double Similarity)>> FindSimilarEventsAsync(
		float[] embedding,
		double threshold,
		int windowHours,
        int maxTake,
        CancellationToken cancellationToken = default)
	{
		var vector = new Vector(embedding);
		var windowStart = DateTimeOffset.UtcNow.AddHours(-windowHours);

		var query = _context.Events
			.Where(e =>
				e.Status == EventStatus.Active.ToString() &&
				e.LastUpdatedAt >= windowStart &&
				e.Embedding != null)
			.Select(e => new
			{
				Entity = e,
				Similarity = 1 - e.Embedding!.CosineDistance(vector)
			})
			.Where(x => x.Similarity >= threshold)
			.OrderByDescending(x => x.Similarity)
			.Take(maxTake);

		return (await query.ToListAsync(cancellationToken))
			.Select(x => (x.Entity.ToDomain(), x.Similarity))
			.ToList();
	}

	public async Task<Event> CreateAsync(Event evt, CancellationToken cancellationToken = default)
	{
		var entity = evt.ToEntity();
		_context.Events.Add(entity);
		await _context.SaveChangesAsync(cancellationToken);
		return entity.ToDomain();
	}

	public async Task UpdateSummaryTitleAndEmbeddingAsync(
		Guid id,
		string title,
		string summary,
		float[] embedding,
		CancellationToken cancellationToken = default)
	{
		await _context.Events
			.Where(e => e.Id == id)
			.ExecuteUpdateAsync(s => s
				.SetProperty(e => e.Title, title)
				.SetProperty(e => e.Summary, summary)
				.SetProperty(e => e.Embedding, new Vector(embedding))
				.SetProperty(e => e.ArticleCount, e => e.ArticleCount + 1)
				.SetProperty(e => e.LastUpdatedAt, DateTimeOffset.UtcNow),
			cancellationToken);
	}

	public async Task UpdateLastUpdatedAtAsync(
		Guid id,
		DateTimeOffset lastUpdatedAt,
		CancellationToken cancellationToken = default)
	{
		await _context.Events
			.Where(e => e.Id == id)
			.ExecuteUpdateAsync(s => s
				.SetProperty(e => e.LastUpdatedAt, lastUpdatedAt),
			cancellationToken);
	}

	public async Task AssignArticleToEventAsync(
		Guid articleId,
		Guid eventId,
		ArticleRole role,
		CancellationToken cancellationToken = default)
	{
		await _context.Articles
			.Where(a => a.Id == articleId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(a => a.EventId, eventId)
				.SetProperty(a => a.Role, role.ToString())
				.SetProperty(a => a.AddedToEventAt, DateTimeOffset.UtcNow),
			cancellationToken);
	}

	public async Task AddEventUpdateAsync(
		EventUpdate eventUpdate,
		CancellationToken cancellationToken = default)
	{
		var entity = eventUpdate.ToEntity();
		_context.EventUpdates.Add(entity);
		await _context.SaveChangesAsync(cancellationToken);
	}

	public async Task AddContradictionAsync(
	Contradiction contradiction,
	List<Guid> articleIds,
	CancellationToken cancellationToken = default)
	{
		var entity = contradiction.ToEntity();
		_context.Contradictions.Add(entity);
		await _context.SaveChangesAsync(cancellationToken);

		var contradictionArticles = articleIds.Select(articleId => new ContradictionArticleEntity
		{
			ContradictionId = entity.Id,
			ArticleId = articleId,
		}).ToList();

		await _context.ContradictionArticles.AddRangeAsync(contradictionArticles, cancellationToken);
		await _context.SaveChangesAsync(cancellationToken);
	}

	public async Task<List<EventUpdate>> GetUnpublishedUpdatesAsync(
		int batchSize,
		CancellationToken cancellationToken = default)
	{
		var entities = await _context.EventUpdates
			.Include(eu => eu.Event)
			.Include(eu => eu.Article)
			.Where(eu => !eu.IsPublished)
			.OrderBy(eu => eu.CreatedAt)
			.Take(batchSize)
			.ToListAsync(cancellationToken);

		return entities.Select(eu => eu.ToDomain()).ToList();
	}

	public async Task MarkUpdatePublishedAsync(
		Guid eventUpdateId,
		CancellationToken cancellationToken = default)
	{
		await _context.EventUpdates
			.Where(eu => eu.Id == eventUpdateId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(eu => eu.IsPublished, true),
			cancellationToken);
	}

	public async Task<int> CountUpdatesFromAsync(
		Guid eventId,
        DateTimeOffset from,
        CancellationToken cancellationToken = default)
	{
		return await _context.EventUpdates
			.CountAsync(eu =>
				eu.EventId == eventId &&
				eu.CreatedAt >= from,
			cancellationToken);
	}

	public async Task<DateTimeOffset?> GetLastUpdateTimeAsync(
		Guid eventId,
		CancellationToken cancellationToken = default)
	{
		return await _context.EventUpdates
			.Where(eu => eu.EventId == eventId)
			.OrderByDescending(eu => eu.CreatedAt)
			.Select(eu => (DateTimeOffset?)eu.CreatedAt)
			.FirstOrDefaultAsync(cancellationToken);
	}

	public async Task<List<Event>> GetPagedAsync(
	int page,
	int pageSize,
	CancellationToken cancellationToken = default)
	{
		var entities = await _context.Events
			.Include(e => e.Articles)
			.Include(e => e.Contradictions)
			.OrderByDescending(e => e.LastUpdatedAt)
			.Skip((page - 1) * pageSize)
			.Take(pageSize)
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public async Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
	{
		return await _context.Events
			.CountAsync(e => e.Status == EventStatus.Active.ToString(), cancellationToken);
	}

	public async Task<Event?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var entity = await _context.Events
			.Include(e => e.Articles)
				.ThenInclude(a => a.MediaFiles)
			.Include(e => e.EventUpdates)
			.Include(e => e.Contradictions)
				.ThenInclude(c => c.ContradictionArticles)
			.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

		return entity?.ToDomain();
	}

	public async Task<Event?> GetWithContextAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var entity = await _context.Events
			.Include(e => e.Articles)
			.Include(e => e.EventUpdates)
			.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

		return entity?.ToDomain();
	}

	public async Task ResolveContradictionAsync(
		Guid contradictionId,
		CancellationToken cancellationToken = default)
	{
		await _context.Contradictions
			.Where(c => c.Id == contradictionId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(c => c.IsResolved, true),
			cancellationToken);
	}

	public async Task MergeAsync(
		Guid sourceEventId,
		Guid targetEventId,
		CancellationToken cancellationToken = default)
	{
		await _context.Articles
			.Where(a => a.EventId == sourceEventId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(a => a.EventId, targetEventId),
			cancellationToken);

		await _context.EventUpdates
			.Where(eu => eu.EventId == sourceEventId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(eu => eu.EventId, targetEventId),
			cancellationToken);

		await _context.Contradictions
			.Where(c => c.EventId == sourceEventId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(c => c.EventId, targetEventId),
			cancellationToken);

		await _context.Events
			.Where(e => e.Id == sourceEventId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(e => e.Status, EventStatus.Archived.ToString()),
			cancellationToken);

		await _context.Events
			.Where(e => e.Id == targetEventId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(e => e.LastUpdatedAt, DateTimeOffset.UtcNow),
			cancellationToken);
	}

	public async Task UpdateArticleRoleAsync(
		Guid articleId,
		ArticleRole role,
		CancellationToken cancellationToken = default)
	{
		await _context.Articles
			.Where(a => a.Id == articleId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(a => a.Role, role.ToString()),
			cancellationToken);
	}

	public async Task UpdateStatusAsync(
		Guid id,
		EventStatus status,
		CancellationToken cancellationToken = default)
	{
		await _context.Events
			.Where(e => e.Id == id)
			.ExecuteUpdateAsync(s => s
				.SetProperty(e => e.Status, status.ToString()),
			cancellationToken);
	}

	public async Task MarkAsReclassifiedAsync(
	Guid articleId,
	CancellationToken cancellationToken = default)
	{
		await _context.Articles
			.Where(a => a.Id == articleId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(a => a.WasReclassified, true),
			cancellationToken);
	}
}