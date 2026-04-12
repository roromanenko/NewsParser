using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;

namespace Infrastructure.Services;

public class PublicationService(
	IEventRepository eventRepository,
	IPublicationRepository publicationRepository,
	IPublishTargetRepository publishTargetRepository) : IPublicationService
{
	public async Task<Publication> CreateForEventAsync(
		Guid eventId,
		Guid publishTargetId,
		Guid editorId,
		CancellationToken cancellationToken = default)
	{
		var relatedEvent = await eventRepository.GetDetailAsync(eventId, cancellationToken)
			?? throw new KeyNotFoundException($"Event {eventId} not found");

		if (relatedEvent.Status != EventStatus.Active)
			throw new InvalidOperationException(
				$"Event {eventId} cannot have content generated: status is {relatedEvent.Status}");

		var publishTarget = await publishTargetRepository.GetByIdAsync(publishTargetId, cancellationToken)
			?? throw new KeyNotFoundException($"PublishTarget {publishTargetId} not found");

		if (!publishTarget.IsActive)
			throw new InvalidOperationException($"PublishTarget {publishTargetId} is not active");

		if (!relatedEvent.Articles.Any())
			throw new InvalidOperationException($"Event {eventId} has no articles");

		var initiatorArticle = relatedEvent.Articles
			.FirstOrDefault(a => a.Role == ArticleRole.Initiator)
			?? relatedEvent.Articles.First();

		var publication = new Publication
		{
			Id = Guid.NewGuid(),
			Article = initiatorArticle,
			PublishTargetId = publishTarget.Id,
			PublishTarget = publishTarget,
			Status = PublicationStatus.Created,
			CreatedAt = DateTimeOffset.UtcNow,
			EventId = relatedEvent.Id,
			Event = relatedEvent,
		};

		await publicationRepository.AddAsync(publication, cancellationToken);

		return publication;
	}

	public async Task<Publication> UpdateContentAsync(
		Guid publicationId,
		string content,
		List<Guid> selectedMediaFileIds,
		CancellationToken cancellationToken = default)
	{
		var publication = await publicationRepository.GetByIdAsync(publicationId, cancellationToken)
			?? throw new KeyNotFoundException($"Publication {publicationId} not found");

		if (publication.Status != PublicationStatus.ContentReady)
			throw new InvalidOperationException(
				$"Publication {publicationId} cannot be updated: status is {publication.Status}");

		await publicationRepository.UpdateContentAndMediaAsync(publicationId, content, selectedMediaFileIds, cancellationToken);

		publication.GeneratedContent = content;
		publication.SelectedMediaFileIds = selectedMediaFileIds;

		return publication;
	}

	public async Task<Publication> ApproveAsync(
		Guid publicationId,
		Guid editorId,
		CancellationToken cancellationToken = default)
	{
		var publication = await publicationRepository.GetByIdAsync(publicationId, cancellationToken)
			?? throw new KeyNotFoundException($"Publication {publicationId} not found");

		if (publication.Status != PublicationStatus.ContentReady)
			throw new InvalidOperationException(
				$"Publication {publicationId} cannot be approved: status is {publication.Status}");

		var approvedAt = DateTimeOffset.UtcNow;
		await publicationRepository.UpdateApprovalAsync(publicationId, editorId, approvedAt, cancellationToken);

		publication.Status = PublicationStatus.Approved;
		publication.ApprovedAt = approvedAt;
		publication.ReviewedByEditorId = editorId;

		return publication;
	}

	public async Task<Publication> RejectAsync(
		Guid publicationId,
		Guid editorId,
		string reason,
		CancellationToken cancellationToken = default)
	{
		var publication = await publicationRepository.GetByIdAsync(publicationId, cancellationToken)
			?? throw new KeyNotFoundException($"Publication {publicationId} not found");

		if (publication.Status is not PublicationStatus.ContentReady and not PublicationStatus.Approved)
			throw new InvalidOperationException(
				$"Publication {publicationId} cannot be rejected: status is {publication.Status}");

		var rejectedAt = DateTimeOffset.UtcNow;
		await publicationRepository.UpdateRejectionAsync(publicationId, editorId, reason, rejectedAt, cancellationToken);

		publication.Status = PublicationStatus.Rejected;
		publication.RejectedAt = rejectedAt;
		publication.RejectionReason = reason;
		publication.ReviewedByEditorId = editorId;

		return publication;
	}

	public async Task<Publication> SendAsync(
		Guid publicationId,
		Guid editorId,
		CancellationToken cancellationToken = default)
	{
		var publication = await publicationRepository.GetByIdAsync(publicationId, cancellationToken)
			?? throw new KeyNotFoundException($"Publication {publicationId} not found");

		if (publication.Status is not PublicationStatus.ContentReady and not PublicationStatus.Approved)
			throw new InvalidOperationException(
				$"Publication {publicationId} cannot be sent: status is {publication.Status}");

		var approvedAt = DateTimeOffset.UtcNow;
		await publicationRepository.UpdateApprovalAsync(publicationId, editorId, approvedAt, cancellationToken);

		publication.Status = PublicationStatus.Approved;
		publication.ApprovedAt = approvedAt;
		publication.ReviewedByEditorId = editorId;

		return publication;
	}
}
