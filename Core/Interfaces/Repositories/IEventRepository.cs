using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IEventRepository
{
	Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	Task<List<Event>> GetActiveEventsAsync(CancellationToken cancellationToken = default);

	// Векторный поиск похожих событий в окне времени
	Task<List<(Event Event, double Similarity)>> FindSimilarEventsAsync(
		float[] embedding,
		double threshold,
		int windowHours,
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

	Task AddArticleAsync(
		EventArticle eventArticle,
		CancellationToken cancellationToken = default);

	Task AddEventUpdateAsync(
		EventUpdate eventUpdate,
		CancellationToken cancellationToken = default);

	Task AddContradictionAsync(
		Contradiction contradiction,
		List<Guid> articleIds,
		CancellationToken cancellationToken = default);

	// Для EventUpdateWorker
	Task<List<EventUpdate>> GetUnpublishedUpdatesAsync(
		int batchSize,
		CancellationToken cancellationToken = default);

	Task MarkUpdatePublishedAsync(
		Guid eventUpdateId,
		CancellationToken cancellationToken = default);

	// Для лимита апдейтов в сутки
	Task<int> CountTodayUpdatesAsync(
		Guid eventId,
		CancellationToken cancellationToken = default);

	// Для минимального интервала между апдейтами
	Task<DateTimeOffset?> GetLastUpdateTimeAsync(
		Guid eventId,
		CancellationToken cancellationToken = default);

	Task<List<Event>> GetPagedAsync(
	int page,
	int pageSize,
	CancellationToken cancellationToken = default);

	Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

	Task<Event?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

	Task ResolveContradictionAsync(
		Guid contradictionId,
		CancellationToken cancellationToken = default);

	Task MergeAsync(
		Guid sourceEventId,
		Guid targetEventId,
		CancellationToken cancellationToken = default);

	Task UpdateArticleRoleAsync(
		Guid eventId,
		Guid articleId,
		EventArticleRole role,
		CancellationToken cancellationToken = default);

	Task UpdateStatusAsync(
		Guid id,
		EventStatus status,
		CancellationToken cancellationToken = default);

	Task MarkAsReclassifiedAsync(
		Guid eventId,
		Guid articleId,
		CancellationToken cancellationToken = default);
}