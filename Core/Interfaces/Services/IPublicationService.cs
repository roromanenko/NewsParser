using Core.DomainModels;

namespace Core.Interfaces.Services;

public interface IPublicationService
{
	Task<Publication> CreateForEventAsync(
		Guid eventId,
		Guid publishTargetId,
		Guid editorId,
		Guid projectId,
		CancellationToken cancellationToken = default);

	Task<Publication> UpdateContentAsync(
		Guid publicationId,
		string content,
		List<Guid> selectedMediaFileIds,
		CancellationToken cancellationToken = default);

	Task<Publication> ApproveAsync(
		Guid publicationId,
		Guid editorId,
		CancellationToken cancellationToken = default);

	Task<Publication> RejectAsync(
		Guid publicationId,
		Guid editorId,
		string reason,
		CancellationToken cancellationToken = default);

	Task<Publication> SendAsync(
		Guid publicationId,
		Guid editorId,
		CancellationToken cancellationToken = default);

	Task<Publication> RegenerateAsync(
		Guid publicationId,
		string feedback,
		CancellationToken cancellationToken = default);
}
