using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IPublicationRepository
{
	/// <summary>
	/// Returns publications with status Created; immediately marks them GenerationInProgress
	/// to prevent double-processing on next worker cycle.
	/// </summary>
	Task<List<Publication>> GetPendingForGenerationAsync(int batchSize, CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns publications with status Approved, ready for the PublishingWorker to send.
	/// </summary>
	Task<List<Publication>> GetPendingForPublishAsync(int batchSize, CancellationToken cancellationToken = default);

	Task AddAsync(Publication publication, CancellationToken cancellationToken = default);
	Task<Publication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task<Publication?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);
	Task<List<Publication>> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default);
	Task<List<Publication>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default);
	Task<int> CountAllAsync(CancellationToken cancellationToken = default);

	Task UpdateStatusAsync(Guid id, PublicationStatus status, CancellationToken cancellationToken = default);
	Task UpdateGeneratedContentAsync(Guid id, string content, CancellationToken cancellationToken = default);
	Task UpdatePublishedAtAsync(Guid id, DateTimeOffset publishedAt, CancellationToken cancellationToken = default);
	Task UpdateContentAndMediaAsync(Guid id, string content, List<Guid> mediaFileIds, CancellationToken cancellationToken = default);
	Task UpdateApprovalAsync(Guid id, Guid editorId, DateTimeOffset approvedAt, CancellationToken cancellationToken = default);
	Task UpdateRejectionAsync(Guid id, Guid editorId, string reason, DateTimeOffset rejectedAt, CancellationToken cancellationToken = default);

	Task AddPublishLogAsync(PublishLog log, CancellationToken cancellationToken = default);
	Task<string?> GetExternalMessageIdAsync(Guid publicationId, CancellationToken cancellationToken = default);
	Task<Publication?> GetOriginalEventPublicationAsync(Guid eventId, CancellationToken cancellationToken = default);
	Task AddEventUpdatePublicationAsync(Publication publication, Guid articleId, CancellationToken cancellationToken = default);
	Task RequestRegenerationAsync(Guid id, string feedback, CancellationToken cancellationToken = default);
}
