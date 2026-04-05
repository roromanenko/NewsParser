using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IArticleRepository
{
	Task AddAsync(Article article, CancellationToken cancellationToken = default);
	Task<Article?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task<List<Article>> GetPendingForApprovalAsync(int page, int pageSize, CancellationToken cancellationToken = default);
	Task<int> CountPendingForApprovalAsync(CancellationToken cancellationToken = default);
	Task UpdateStatusAsync(Guid id, ArticleStatus status, CancellationToken cancellationToken = default);
	Task UpdateRejectionAsync(Guid id, Guid editorId, string reason, CancellationToken cancellationToken = default);
	Task IncrementRetryAsync(Guid id, CancellationToken cancellationToken = default);

	// Locking queries for race-condition-free processing
	Task<List<Article>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default);
	Task<List<Article>> GetPendingForClassificationAsync(int batchSize, CancellationToken cancellationToken = default);

	// Analysis result update
	Task UpdateKeyFactsAsync(Guid id, List<string> keyFacts, CancellationToken cancellationToken = default);
	Task UpdateAnalysisResultAsync(Guid id, string category, List<string> tags, string sentiment,
		string language, string summary, string modelVersion,
		CancellationToken cancellationToken = default);

	// Embedding and deduplication
	Task UpdateEmbeddingAsync(Guid id, float[] embedding, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid sourceId, string externalId, CancellationToken cancellationToken = default);
	Task<bool> ExistsByUrlAsync(string url, CancellationToken cancellationToken = default);
	Task<List<string>> GetRecentTitlesForDeduplicationAsync(int windowHours, CancellationToken cancellationToken = default);
}
