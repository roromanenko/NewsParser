using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IEventRepository
{
	Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	Task<List<Event>> GetActiveEventsAsync(CancellationToken cancellationToken = default);

	Task<List<(Event Event, double Similarity)>> FindSimilarEventsAsync(
		float[] embedding,
		double threshold,
		int windowHours,
		int maxTake,
		CancellationToken cancellationToken = default);

	Task<Event> CreateAsync(Event evt, CancellationToken cancellationToken = default);

	Task UpdateSummaryAndEmbeddingAsync(
		Guid id,
		string summary,
		float[] embedding,
		CancellationToken cancellationToken = default);

	Task UpdateLastUpdatedAtAsync(
		Guid id,
		DateTimeOffset lastUpdatedAt,
		CancellationToken cancellationToken = default);

	Task AssignArticleToEventAsync(
		Guid articleId,
		Guid eventId,
		ArticleRole role,
		CancellationToken cancellationToken = default);

	Task AddEventUpdateAsync(
		EventUpdate eventUpdate,
		CancellationToken cancellationToken = default);

	Task AddContradictionAsync(
		Contradiction contradiction,
		List<Guid> articleIds,
		CancellationToken cancellationToken = default);

	Task<List<EventUpdate>> GetUnpublishedUpdatesAsync(
		int batchSize,
		CancellationToken cancellationToken = default);

	Task MarkUpdatePublishedAsync(
		Guid eventUpdateId,
		CancellationToken cancellationToken = default);

	Task<int> CountUpdatesFromAsync(
		Guid eventId,
		DateTimeOffset from,
        CancellationToken cancellationToken = default);

	Task<DateTimeOffset?> GetLastUpdateTimeAsync(
		Guid eventId,
		CancellationToken cancellationToken = default);

	Task<List<Event>> GetPagedAsync(
	int page,
	int pageSize,
	CancellationToken cancellationToken = default);

	Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

	Task<Event?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

	Task<Event?> GetWithContextAsync(Guid id, CancellationToken cancellationToken = default);

	Task ResolveContradictionAsync(
		Guid contradictionId,
		CancellationToken cancellationToken = default);

	Task MergeAsync(
		Guid sourceEventId,
		Guid targetEventId,
		CancellationToken cancellationToken = default);

	Task UpdateArticleRoleAsync(
		Guid articleId,
		ArticleRole role,
		CancellationToken cancellationToken = default);

	Task UpdateStatusAsync(
		Guid id,
		EventStatus status,
		CancellationToken cancellationToken = default);

	Task MarkAsReclassifiedAsync(
		Guid articleId,
		CancellationToken cancellationToken = default);
}