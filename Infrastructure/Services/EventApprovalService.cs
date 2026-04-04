using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;

namespace Infrastructure.Services;

public class EventApprovalService(
	IEventRepository eventRepository,
	IArticleRepository articleRepository,
	IPublicationRepository publicationRepository,
	IPublishTargetRepository publishTargetRepository) : IEventApprovalService
{
	public async Task<Event> ApproveAsync(
		Guid eventId,
		Guid editorId,
		List<Guid> publishTargetIds,
		CancellationToken cancellationToken = default)
	{
		var relatedEvent = await eventRepository.GetDetailAsync(eventId, cancellationToken)
			?? throw new KeyNotFoundException($"Event {eventId} not found");

		if (relatedEvent.Status != EventStatus.Active)
			throw new InvalidOperationException(
				$"Event {eventId} cannot be approved: status is {relatedEvent.Status}");

		var publishTargets = await ValidateAndLoadTargetsAsync(publishTargetIds, cancellationToken);

		if (!relatedEvent.Articles.Any())
			throw new InvalidOperationException($"Event {eventId} has no articles.");

		var initiatorArticle = relatedEvent.Articles
			.FirstOrDefault(a => a.Role == ArticleRole.Initiator)
			?? relatedEvent.Articles.First();

		var publications = publishTargets
			.Select(target => new Publication
			{
				Id = Guid.NewGuid(),
				Article = initiatorArticle,
				PublishTargetId = target.Id,
				PublishTarget = target,
				Status = PublicationStatus.Pending,
				CreatedAt = DateTimeOffset.UtcNow,
				ApprovedAt = DateTimeOffset.UtcNow,
				EventId = relatedEvent.Id,
			})
			.ToList();

		await publicationRepository.AddRangeAsync(initiatorArticle.Id, editorId, publications, cancellationToken);
		await eventRepository.UpdateStatusAsync(eventId, EventStatus.Approved, cancellationToken);

		foreach (var article in relatedEvent.Articles)
		{
			await articleRepository.UpdateStatusAsync(article.Id, ArticleStatus.Approved, cancellationToken);
		}

		relatedEvent.Status = EventStatus.Approved;
		return relatedEvent;
	}

	public async Task<Event> RejectAsync(
		Guid eventId,
		Guid editorId,
		string reason,
		CancellationToken cancellationToken = default)
	{
		var relatedEvent = await eventRepository.GetDetailAsync(eventId, cancellationToken)
			?? throw new KeyNotFoundException($"Event {eventId} not found");

		await eventRepository.UpdateStatusAsync(eventId, EventStatus.Rejected, cancellationToken);

		foreach (var article in relatedEvent.Articles)
		{
			await articleRepository.UpdateStatusAsync(article.Id, ArticleStatus.Rejected, cancellationToken);
		}

		relatedEvent.Status = EventStatus.Rejected;
		return relatedEvent;
	}

	private async Task<List<PublishTarget>> ValidateAndLoadTargetsAsync(
		List<Guid> publishTargetIds,
		CancellationToken cancellationToken)
	{
		var publishTargets = new List<PublishTarget>();

		foreach (var targetId in publishTargetIds)
		{
			var target = await publishTargetRepository.GetByIdAsync(targetId, cancellationToken)
				?? throw new KeyNotFoundException($"PublishTarget {targetId} not found");

			if (!target.IsActive)
				throw new InvalidOperationException($"PublishTarget {targetId} is not active");

			publishTargets.Add(target);
		}

		return publishTargets;
	}
}
