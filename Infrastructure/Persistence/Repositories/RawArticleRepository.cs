using Core.DomainModels;
using Core.Interfaces.Repositories;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class RawArticleRepository : IRawArticleRepository
{
	private readonly NewsParserDbContext _context;

	public RawArticleRepository(NewsParserDbContext context)
	{
		_context = context;
	}

	public async Task<bool> ExistsAsync(Guid sourceId, string externalId, CancellationToken cancellationToken = default)
	{
		return await _context.RawArticles
			.AnyAsync(r => r.SourceId == sourceId && r.ExternalId == externalId, cancellationToken);
	}

	public async Task AddAsync(RawArticle rawArticle, CancellationToken cancellationToken = default)
	{
		var entity = rawArticle.ToEntity();
		await _context.RawArticles.AddAsync(entity, cancellationToken);
		await _context.SaveChangesAsync(cancellationToken);
	}

	public async Task<bool> HasSimilarAsync(
		Guid currentId,
		float[] embedding,
		double threshold,
		int windowHours,
		CancellationToken cancellationToken = default)
	{
		var vector = new Vector(embedding);
		var since = DateTimeOffset.UtcNow.AddHours(-windowHours);

		return await _context.RawArticles
			.Where(r => r.Id != currentId
				&& r.PublishedAt >= since
				&& r.Embedding != null
				&& r.Status != RawArticleStatus.Rejected.ToString())
			.AnyAsync(r => 1 - r.Embedding!.CosineDistance(vector) >= threshold, cancellationToken);
	}

	public async Task UpdateEmbeddingAsync(Guid id, float[] embedding, CancellationToken cancellationToken = default)
	{
		var vector = new Vector(embedding);
		await _context.RawArticles
			.Where(r => r.Id == id)
			.ExecuteUpdateAsync(r => r.SetProperty(x => x.Embedding, vector), cancellationToken);
	}

	public async Task<List<string>> GetRecentTitlesAsync(Guid currentId, int windowHours, CancellationToken cancellationToken = default)
	{
		var since = DateTimeOffset.UtcNow.AddHours(-windowHours);
		return await _context.RawArticles
			.Where(r => r.Id != currentId
				&& r.PublishedAt >= since
				&& r.Status != RawArticleStatus.Rejected.ToString())
			.Select(r => r.Title)
			.ToListAsync(cancellationToken);
	}
}