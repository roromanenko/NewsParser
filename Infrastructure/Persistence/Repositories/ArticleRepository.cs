using Core.DomainModels;
using Core.Interfaces.Repositories;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

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

	public async Task<List<RawArticle>> GetPendingForAnalysisAsync(int batchSize, CancellationToken cancellationToken = default)
	{
		var entities = await _context.RawArticles
			.Where(r => r.Status == RawArticleStatus.Pending.ToString())
			.OrderBy(r => r.PublishedAt)
			.Take(batchSize)
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public async Task<List<Article>> GetPendingForGenerationAsync(
		int batchSize,
		CancellationToken cancellationToken = default)
	{
		var entities = await _context.Articles
			.Include(a => a.RawArticle)
			.Where(a => a.Status == ArticleStatus.AnalysisDone.ToString())
			.Where(a => a.EventId != null)
			.OrderBy(a => a.ProcessedAt)
			.Take(batchSize)
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public async Task<Article?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var entity = await _context.Articles
			.Include(a => a.RawArticle)
			.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

		return entity?.ToDomain();
	}

	public async Task<List<Article>> GetPendingForApprovalAsync(int page, int pageSize, CancellationToken cancellationToken = default)
	{
		var entities = await _context.Articles
			.Include(a => a.RawArticle)
			.Where(a => a.Status == ArticleStatus.Pending.ToString())
			.OrderByDescending(a => a.ProcessedAt)
			.Skip((page - 1) * pageSize)
			.Take(pageSize)
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public async Task<List<Article>> GetPendingForClassificationAsync(
	int batchSize,
	CancellationToken cancellationToken = default)
	{
		var entities = await _context.Articles
			.Include(a => a.RawArticle)
			.Where(a => a.Status == ArticleStatus.AnalysisDone.ToString())
			.Where(a => a.EventId == null)
			.OrderBy(a => a.ProcessedAt)
			.Take(batchSize)
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public async Task<int> CountPendingForApprovalAsync(CancellationToken cancellationToken = default)
	{
		return await _context.Articles
			.CountAsync(a => a.Status == ArticleStatus.Pending.ToString(), cancellationToken);
	}

	public async Task UpdateStatusAsync(Guid id, ArticleStatus status, CancellationToken cancellationToken = default)
	{
		await _context.Articles
			.Where(a => a.Id == id)
			.ExecuteUpdateAsync(a => a.SetProperty(x => x.Status, status.ToString()), cancellationToken);
	}

	public async Task UpdateRawArticleStatusAsync(Guid id, RawArticleStatus status, CancellationToken cancellationToken = default)
	{
		await _context.RawArticles
			.Where(r => r.Id == id)
			.ExecuteUpdateAsync(r => r.SetProperty(x => x.Status, status.ToString()), cancellationToken);
	}

	public async Task UpdateGeneratedContentAsync(Guid id, string title, string content, ArticleStatus status, CancellationToken cancellationToken = default)
	{
		await _context.Articles
			.Where(a => a.Id == id)
			.ExecuteUpdateAsync(a => a
				.SetProperty(x => x.Title, title)
				.SetProperty(x => x.Content, content)
				.SetProperty(x => x.Status, status.ToString()),
			cancellationToken);
	}

	public async Task UpdateRejectionAsync(Guid id, Guid editorId, string reason, CancellationToken cancellationToken = default)
	{
		await _context.Articles
			.Where(a => a.Id == id)
			.ExecuteUpdateAsync(a => a
				.SetProperty(x => x.Status, ArticleStatus.Rejected.ToString())
				.SetProperty(x => x.RejectedByEditorId, editorId)
				.SetProperty(x => x.RejectionReason, reason),
			cancellationToken);
	}

	public async Task IncrementRawArticleRetryAsync(Guid id, CancellationToken cancellationToken = default)
	{
		await _context.RawArticles
			.Where(r => r.Id == id)
			.ExecuteUpdateAsync(r => r
				.SetProperty(x => x.RetryCount, x => x.RetryCount + 1),
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
}