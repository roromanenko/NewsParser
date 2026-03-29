using Core.DomainModels;

namespace Core.Interfaces.Services;

public interface IEventService
{
	Task MergeAsync(
		Guid sourceEventId,
		Guid targetEventId,
		CancellationToken cancellationToken = default);

	Task ReclassifyArticleAsync(
		Guid eventId,
		Guid articleId,
		EventArticleRole role,
		CancellationToken cancellationToken = default);
}