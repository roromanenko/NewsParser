using Core.DomainModels;
using Core.Interfaces;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class PublicationService(
	IEventRepository eventRepository,
	IPublicationRepository publicationRepository,
	IPublishTargetRepository publishTargetRepository,
	IMediaFileRepository mediaFileRepository,
	IProjectContext projectContext,
	ILogger<PublicationService> logger) : IPublicationService
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

		if (projectContext.IsSet && initiatorArticle.ProjectId != projectContext.ProjectId)
			throw new InvalidOperationException("Article does not belong to the current project");

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
			ProjectId = projectContext.IsSet ? projectContext.ProjectId : initiatorArticle.ProjectId,
		};

		await publicationRepository.AddAsync(publication, cancellationToken);
		logger.LogInformation("Publication {PublicationId} created for event {EventId} by editor {EditorId}",
			publication.Id, eventId, editorId);

		return publication;
	}

	public async Task<Publication> UpdateContentAsync(
		Guid publicationId,
		string content,
		List<Guid> selectedMediaFileIds,
		CancellationToken cancellationToken = default)
	{
		var publication = await publicationRepository.GetDetailAsync(publicationId, cancellationToken)
			?? throw new KeyNotFoundException($"Publication {publicationId} not found");

		if (publication.Status != PublicationStatus.ContentReady)
			throw new InvalidOperationException(
				$"Publication {publicationId} cannot be updated: status is {publication.Status}");

		var eligibleIds = (await mediaFileRepository.GetByPublicationIdAsync(publicationId, cancellationToken))
			.Select(m => m.Id)
			.Concat(publication.Event?.Articles.SelectMany(a => a.MediaFiles).Select(m => m.Id) ?? [])
			.ToHashSet();

		var invalid = selectedMediaFileIds.Where(id => !eligibleIds.Contains(id)).ToList();
		if (invalid.Count > 0)
			throw new ArgumentException($"Media file ids not eligible for this publication: {string.Join(",", invalid)}");

		await publicationRepository.UpdateContentAndMediaAsync(publicationId, content, selectedMediaFileIds, cancellationToken);
		logger.LogInformation("Publication {PublicationId} content updated by editor {EditorId}",
			publicationId, publication.ReviewedByEditorId);

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
		logger.LogInformation("Publication {PublicationId} status set to {NewStatus} by editor {EditorId}",
			publicationId, PublicationStatus.Approved, editorId);

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
		logger.LogInformation("Publication {PublicationId} status set to {NewStatus} by editor {EditorId}",
			publicationId, PublicationStatus.Rejected, editorId);

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
		logger.LogInformation("Publication {PublicationId} status set to {NewStatus} by editor {EditorId}",
			publicationId, PublicationStatus.Approved, editorId);

		publication.Status = PublicationStatus.Approved;
		publication.ApprovedAt = approvedAt;
		publication.ReviewedByEditorId = editorId;

		return publication;
	}

	public async Task<Publication> RegenerateAsync(
		Guid publicationId,
		string feedback,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(feedback))
			throw new ArgumentException("Feedback must not be empty", nameof(feedback));

		var publication = await publicationRepository.GetByIdAsync(publicationId, cancellationToken)
			?? throw new KeyNotFoundException($"Publication {publicationId} not found");

		if (publication.Status is not PublicationStatus.ContentReady and not PublicationStatus.Failed)
			throw new InvalidOperationException(
				$"Publication {publicationId} cannot be regenerated: status is {publication.Status}");

		await publicationRepository.RequestRegenerationAsync(publicationId, feedback, cancellationToken);
		logger.LogInformation("Publication {PublicationId} queued for regeneration with editor feedback",
			publicationId);

		publication.Status = PublicationStatus.Created;
		publication.EditorFeedback = feedback;
		publication.GeneratedContent = string.Empty;

		return publication;
	}
}
