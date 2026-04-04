using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IPublicationRepository
{
	Task AddRangeAsync(Guid articleId, Guid editorId, List<Publication> publications, CancellationToken cancellationToken = default);
	/// <summary>
	/// Returns pending publications with Article, PublishTarget, and Event (with Articles) loaded.
	/// Event and its Articles are required for event-based content generation.
	/// </summary>
	Task<List<Publication>> GetPendingForContentGenerationAsync(int batchSize, CancellationToken cancellationToken = default);
	Task<List<Publication>> GetReadyForPublishAsync(int batchSize, CancellationToken cancellationToken = default);
	Task UpdateStatusAsync(Guid id, PublicationStatus status, CancellationToken cancellationToken = default);
	Task UpdateGeneratedContentAsync(Guid id, string content, CancellationToken cancellationToken = default);
	Task UpdatePublishedAtAsync(Guid id, DateTimeOffset publishedAt, CancellationToken cancellationToken = default);
	Task AddPublishLogAsync(PublishLog log, CancellationToken cancellationToken = default);
	Task<string?> GetExternalMessageIdAsync(
		Guid publicationId,
		CancellationToken cancellationToken = default);
	Task<Publication?> GetOriginalEventPublicationAsync(
	Guid eventId,
	CancellationToken cancellationToken = default);

	Task AddEventUpdatePublicationAsync(
		Publication publication,
		Guid articleId,
		CancellationToken cancellationToken = default);
}