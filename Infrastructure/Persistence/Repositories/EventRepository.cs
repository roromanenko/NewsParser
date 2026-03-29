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
			.Include(e => e.EventArticles)
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
			.Include(e => e.EventArticles)
			.OrderByDescending(e => e.LastUpdatedAt)
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public async Task<List<(Event Event, double Similarity)>> FindSimilarEventsAsync(
		float[] embedding,
		double threshold,
		int windowHours,
		CancellationToken cancellationToken = default)
	{
		var vector = new Vector(embedding);
		var windowStart = DateTimeOffset.UtcNow.AddHours(-windowHours);

		var results = await _context.Events
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
			.ToListAsync(cancellationToken);

		return results
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

	public async Task UpdateSummaryAndEmbeddingAsync(
		Guid id,
		string summary,
		float[] embedding,
		CancellationToken cancellationToken = default)
	{
		await _context.Events
			.Where(e => e.Id == id)
			.ExecuteUpdateAsync(s => s
				.SetProperty(e => e.Summary, summary)
				.SetProperty(e => e.Embedding, new Vector(embedding))
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

	public async Task AddArticleAsync(
		EventArticle eventArticle,
		CancellationToken cancellationToken = default)
	{
		var entity = eventArticle.ToEntity();
		_context.EventArticles.Add(entity);
		await _context.SaveChangesAsync(cancellationToken);
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
			.Where(eu => !eu.IsPublished)
			.OrderBy(eu => eu.CreatedAt)
			.Take(batchSize)
			.Include(eu => eu.Event)
			.Include(eu => eu.Article)
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

	public async Task<int> CountTodayUpdatesAsync(
		Guid eventId,
		CancellationToken cancellationToken = default)
	{
		var startOfDay = DateTimeOffset.UtcNow.Date;
		return await _context.EventUpdates
			.CountAsync(eu =>
				eu.EventId == eventId &&
				eu.CreatedAt >= startOfDay,
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
			.Include(e => e.EventArticles)
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
			.Include(e => e.EventArticles)
				.ThenInclude(ea => ea.Article)
			.Include(e => e.EventUpdates)
			.Include(e => e.Contradictions)
				.ThenInclude(c => c.ContradictionArticles)
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
		// Переносим все EventArticle из source в target
		await _context.EventArticles
			.Where(ea => ea.EventId == sourceEventId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(ea => ea.EventId, targetEventId),
			cancellationToken);

		// Переносим EventUpdate
		await _context.EventUpdates
			.Where(eu => eu.EventId == sourceEventId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(eu => eu.EventId, targetEventId),
			cancellationToken);

		// Переносим Contradiction
		await _context.Contradictions
			.Where(c => c.EventId == sourceEventId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(c => c.EventId, targetEventId),
			cancellationToken);

		// Архивируем source событие
		await _context.Events
			.Where(e => e.Id == sourceEventId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(e => e.Status, EventStatus.Archived.ToString()),
			cancellationToken);

		// Обновляем LastUpdatedAt target
		await _context.Events
			.Where(e => e.Id == targetEventId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(e => e.LastUpdatedAt, DateTimeOffset.UtcNow),
			cancellationToken);
	}

	public async Task UpdateArticleRoleAsync(
		Guid eventId,
		Guid articleId,
		EventArticleRole role,
		CancellationToken cancellationToken = default)
	{
		await _context.EventArticles
			.Where(ea => ea.EventId == eventId && ea.ArticleId == articleId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(ea => ea.Role, role.ToString()),
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
	Guid eventId,
	Guid articleId,
	CancellationToken cancellationToken = default)
	{
		await _context.EventArticles
			.Where(ea => ea.EventId == eventId && ea.ArticleId == articleId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(ea => ea.WasReclassified, true),
			cancellationToken);
	}
}