using Core.DomainModels;
using Core.Interfaces.Repositories;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class ArticleRepository : IArticleRepository
{
	private readonly NewsParserDbContext _context;

	public ArticleRepository(NewsParserDbContext context)
	{
		_context = context;
	}

	public async Task AddAsync(Article article, CancellationToken cancellationToken = default)
	{
		var entity = article.ToEntity();
		await _context.Articles.AddAsync(entity, cancellationToken);
		await _context.SaveChangesAsync(cancellationToken);
	}

	public async Task<Article?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var entity = await _context.Articles
			.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

		return entity?.ToDomain();
	}

	public async Task<List<Article>> GetAnalysisDoneAsync(int page, int pageSize, CancellationToken cancellationToken = default)
	{
		var entities = await _context.Articles
			.Where(a => a.Status == ArticleStatus.AnalysisDone.ToString())
			.OrderByDescending(a => a.ProcessedAt)
			.Skip((page - 1) * pageSize)
			.Take(pageSize)
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public async Task<int> CountAnalysisDoneAsync(CancellationToken cancellationToken = default)
	{
		return await _context.Articles
			.CountAsync(a => a.Status == ArticleStatus.AnalysisDone.ToString(), cancellationToken);
	}

	public async Task UpdateStatusAsync(Guid id, ArticleStatus status, CancellationToken cancellationToken = default)
	{
		await _context.Articles
			.Where(a => a.Id == id)
			.ExecuteUpdateAsync(a => a.SetProperty(x => x.Status, status.ToString()), cancellationToken);
	}

	public async Task RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default)
	{
		await _context.Articles
			.Where(a => a.Id == id)
			.ExecuteUpdateAsync(a => a
				.SetProperty(x => x.Status, ArticleStatus.Rejected.ToString())
				.SetProperty(x => x.RejectionReason, reason),
			cancellationToken);
	}

	public async Task IncrementRetryAsync(Guid id, CancellationToken cancellationToken = default)
	{
		await _context.Articles
			.Where(a => a.Id == id)
			.ExecuteUpdateAsync(a => a
				.SetProperty(x => x.RetryCount, x => x.RetryCount + 1),
			cancellationToken);
	}

	public async Task<List<Article>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default)
	{
		var statusStr = ArticleStatus.Pending.ToString();
		var entities = await _context.Articles
			.FromSql(
				$"SELECT * FROM articles WHERE \"Status\" = {statusStr} ORDER BY \"ProcessedAt\" LIMIT {batchSize} FOR UPDATE SKIP LOCKED")
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public async Task<List<Article>> GetPendingForClassificationAsync(int batchSize, CancellationToken cancellationToken = default)
	{
		var statusStr = ArticleStatus.AnalysisDone.ToString();
		var entities = await _context.Articles
			.FromSql(
				$"SELECT * FROM articles WHERE \"Status\" = {statusStr} AND \"EventId\" IS NULL ORDER BY \"ProcessedAt\" LIMIT {batchSize} FOR UPDATE SKIP LOCKED")
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public async Task UpdateKeyFactsAsync(Guid id, List<string> keyFacts, CancellationToken cancellationToken = default)
	{
		await _context.Articles
			.Where(a => a.Id == id)
			.ExecuteUpdateAsync(a => a.SetProperty(x => x.KeyFacts, keyFacts), cancellationToken);
	}

	public async Task UpdateAnalysisResultAsync(
		Guid id, string category, List<string> tags, string sentiment,
		string language, string summary, string modelVersion,
		CancellationToken cancellationToken = default)
	{
		await _context.Articles
			.Where(a => a.Id == id)
			.ExecuteUpdateAsync(a => a
				.SetProperty(x => x.Category, category)
				.SetProperty(x => x.Tags, tags)
				.SetProperty(x => x.Sentiment, sentiment)
				.SetProperty(x => x.Language, language)
				.SetProperty(x => x.Summary, summary)
				.SetProperty(x => x.ModelVersion, modelVersion),
			cancellationToken);
	}

	public async Task UpdateEmbeddingAsync(Guid id, float[] embedding, CancellationToken cancellationToken = default)
	{
		var vector = new Vector(embedding);
		await _context.Articles
			.Where(a => a.Id == id)
			.ExecuteUpdateAsync(a => a.SetProperty(x => x.Embedding, vector), cancellationToken);
	}

	public async Task<bool> ExistsAsync(Guid sourceId, string externalId, CancellationToken cancellationToken = default)
	{
		return await _context.Articles
			.AnyAsync(a => a.SourceId == sourceId && a.ExternalId == externalId, cancellationToken);
	}

	public async Task<bool> ExistsByUrlAsync(string url, CancellationToken cancellationToken = default)
	{
		return await _context.Articles
			.AnyAsync(a => a.OriginalUrl == url
				&& a.Status != ArticleStatus.Rejected.ToString(),
			cancellationToken);
	}

	public async Task<List<string>> GetRecentTitlesForDeduplicationAsync(int windowHours, CancellationToken cancellationToken = default)
	{
		var since = DateTimeOffset.UtcNow.AddHours(-windowHours);
		return await _context.Articles
			.Where(a => a.PublishedAt >= since
				&& a.Status != ArticleStatus.Rejected.ToString())
			.Select(a => a.Title)
			.ToListAsync(cancellationToken);
	}

}
