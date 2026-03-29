using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IArticleRepository
{
	Task AddAsync(Article article, CancellationToken cancellationToken = default);
	Task<List<RawArticle>> GetPendingForAnalysisAsync(int batchSize, CancellationToken cancellationToken = default);
	Task<List<Article>> GetPendingForGenerationAsync(int batchSize, CancellationToken cancellationToken = default);
	Task<Article?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task<List<Article>> GetPendingForApprovalAsync(int page, int pageSize, CancellationToken cancellationToken = default);
	Task<int> CountPendingForApprovalAsync(CancellationToken cancellationToken = default);
	Task UpdateStatusAsync(Guid id, ArticleStatus status, CancellationToken cancellationToken = default);
	Task UpdateRawArticleStatusAsync(Guid id, RawArticleStatus status, CancellationToken cancellationToken = default);
	Task UpdateGeneratedContentAsync(Guid id, string title, string content, ArticleStatus status, CancellationToken cancellationToken = default);
	Task UpdateRejectionAsync(Guid id, Guid editorId, string reason, CancellationToken cancellationToken = default);
	Task IncrementRawArticleRetryAsync(Guid id, CancellationToken cancellationToken = default);
	Task IncrementRetryAsync(Guid id, CancellationToken cancellationToken = default);
	Task<List<Article>> GetPendingForClassificationAsync(int batchSize, CancellationToken cancellationToken = default);
}