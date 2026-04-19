using Core.DomainModels;
using Core.Interfaces.AI;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Infrastructure.Persistence.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

internal class EventService(
	IEventRepository eventRepository,
	IGeminiEmbeddingService embeddingService,
	IEventSummaryUpdater summaryUpdater,
	IEventTitleGenerator titleGenerator,
	IUnitOfWork uow,
	ILogger<EventService> logger) : IEventService
{
	public async Task MergeAsync(
		Guid sourceEventId,
		Guid targetEventId,
		CancellationToken cancellationToken = default)
	{
		var source = await eventRepository.GetByIdAsync(sourceEventId, cancellationToken)
			?? throw new KeyNotFoundException($"Source event {sourceEventId} not found");

		var target = await eventRepository.GetByIdAsync(targetEventId, cancellationToken)
			?? throw new KeyNotFoundException($"Target event {targetEventId} not found");

		if (source.Status == EventStatus.Archived)
			throw new InvalidOperationException(
				$"Source event {sourceEventId} is already archived");

		await uow.BeginAsync(cancellationToken);

		try
		{
			await eventRepository.MergeAsync(sourceEventId, targetEventId, cancellationToken);
			await uow.CommitAsync(cancellationToken);
			logger.LogInformation("Event {TargetEventId} merged from {SourceEventId}", targetEventId, sourceEventId);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			await uow.RollbackAsync(cancellationToken);
			throw;
		}

		try
		{
			// importance recalc on merge is deferred to the roadmap refresher worker
			var summaryResult = await summaryUpdater.UpdateSummaryAsync(
				target, [source.Summary], cancellationToken);

			var mergedTitle = await titleGenerator.GenerateTitleAsync(
				summaryResult.UpdatedSummary,
				[source.Title, target.Title],
				cancellationToken);
			var finalTitle = string.IsNullOrWhiteSpace(mergedTitle) ? target.Title : mergedTitle;

			var newEmbedding = await embeddingService.GenerateEmbeddingAsync(
				summaryResult.UpdatedSummary, cancellationToken);

			await eventRepository.UpdateSummaryTitleAndEmbeddingAsync(
				targetEventId,
				finalTitle,
				summaryResult.UpdatedSummary,
				newEmbedding,
				cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "AI enrichment failed after merging event {SourceId} into {TargetId}; merge succeeded",
				sourceEventId, targetEventId);
		}
	}

	public async Task ReclassifyArticleAsync(
	Guid currentEventId,
	Guid articleId,
	Guid targetEventId,
	ArticleRole role,
	CancellationToken cancellationToken = default)
	{
		var evt = await eventRepository.GetByIdAsync(currentEventId, cancellationToken)
			?? throw new KeyNotFoundException($"Event {currentEventId} not found");

		var article = evt.Articles.FirstOrDefault(a => a.Id == articleId)
			?? throw new KeyNotFoundException(
				$"Article {articleId} is not associated with event {currentEventId}");

		if (currentEventId == targetEventId && article.Role == role)
			return;

		await eventRepository.AssignArticleToEventAsync(
			articleId, targetEventId, role, cancellationToken);

		await eventRepository.MarkAsReclassifiedAsync(articleId, cancellationToken);
		logger.LogInformation("Article {ArticleId} reclassified to event {EventId}", articleId, targetEventId);
	}
}