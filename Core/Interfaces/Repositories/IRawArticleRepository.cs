using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IRawArticleRepository
{
	Task<bool> ExistsAsync(Guid sourceId, string externalId, CancellationToken cancellationToken = default);
	Task AddAsync(RawArticle rawArticle, CancellationToken cancellationToken = default);
	Task<bool> HasSimilarAsync(Guid currentId, float[] embedding, double threshold, int windowHours, CancellationToken cancellationToken = default);
	Task UpdateEmbeddingAsync(Guid id, float[] embedding, CancellationToken cancellationToken = default);
	Task<List<string>> GetRecentTitlesAsync(Guid currentId, int windowHours, CancellationToken cancellationToken = default);
}