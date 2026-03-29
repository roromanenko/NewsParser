using Core.DomainModels;
using Core.Interfaces.AI;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;

namespace Infrastructure.Services;

public class EventService(
	IEventRepository eventRepository,
	IGeminiEmbeddingService embeddingService) : IEventService
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

		// Мёрджим в репозитории
		await eventRepository.MergeAsync(sourceEventId, targetEventId, cancellationToken);

		// Перегенерируем embedding target события на основе объединённого summary
		var mergedSummary = $"{target.Summary} {source.Summary}".Trim();
		var embeddingText = $"{target.Title}. {mergedSummary}";

		try
		{
			var newEmbedding = await embeddingService.GenerateEmbeddingAsync(
				embeddingText, cancellationToken);

			await eventRepository.UpdateSummaryAndEmbeddingAsync(
				targetEventId,
				target.Summary, // summary не меняем — редактор сам обновит если нужно
				newEmbedding,
				cancellationToken);
		}
		catch
		{
			// Не блокируем мёрдж если embedding упал — это некритично
		}
	}

	public async Task ReclassifyArticleAsync(
	Guid eventId,
	Guid articleId,
	EventArticleRole role,
	CancellationToken cancellationToken = default)
	{
		var evt = await eventRepository.GetByIdAsync(eventId, cancellationToken)
			?? throw new KeyNotFoundException($"Event {eventId} not found");

		var eventArticle = evt.EventArticles.FirstOrDefault(ea => ea.ArticleId == articleId)
			?? throw new KeyNotFoundException(
				$"Article {articleId} is not associated with event {eventId}");

		if (eventArticle.Role == role)
			return;

		await eventRepository.UpdateArticleRoleAsync(
			eventId, articleId, role, cancellationToken);

		await eventRepository.MarkAsReclassifiedAsync(
			eventId, articleId, cancellationToken);
	}
}