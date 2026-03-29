using Core.DomainModels;
using Core.Interfaces.AI;
using Core.Interfaces.Repositories;
using Microsoft.Extensions.Options;
using Worker.Configuration;

namespace Worker.Workers;

public class EventClassificationWorker : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<EventClassificationWorker> _logger;
	private readonly EventClassificationOptions _options;

	public EventClassificationWorker(
		IServiceScopeFactory scopeFactory,
		ILogger<EventClassificationWorker> logger,
		IOptions<EventClassificationOptions> options)
	{
		_scopeFactory = scopeFactory;
		_logger = logger;
		_options = options.Value;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			await ProcessAsync(stoppingToken);
			await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
		}
	}

	private async Task ProcessAsync(CancellationToken cancellationToken)
	{
		using var scope = _scopeFactory.CreateScope();
		var articleRepository = scope.ServiceProvider.GetRequiredService<IArticleRepository>();
		var eventRepository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
		var classifier = scope.ServiceProvider.GetRequiredService<IEventClassifier>();
		var embeddingService = scope.ServiceProvider.GetRequiredService<IGeminiEmbeddingService>();
		var summaryUpdater = scope.ServiceProvider.GetRequiredService<IEventSummaryUpdater>();

		var articles = await articleRepository.GetPendingForClassificationAsync(
			_options.BatchSize,
			cancellationToken);

		if (articles.Count == 0)
		{
			_logger.LogInformation("No articles pending for event classification");
			return;
		}

		_logger.LogInformation("Found {Count} articles for event classification", articles.Count);

		foreach (var article in articles)
		{
			await ProcessArticleAsync(
				article,
				articleRepository,
				eventRepository,
				classifier,
				embeddingService,
				summaryUpdater,
				cancellationToken);
		}
	}

	private async Task ProcessArticleAsync(
	Article article,
	IArticleRepository articleRepository,
	IEventRepository eventRepository,
	IEventClassifier classifier,
	IGeminiEmbeddingService embeddingService,
	IEventSummaryUpdater summaryUpdater,
	CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation(
				"Classifying article {Id}: {Title}", article.Id, article.Title);

			await articleRepository.UpdateStatusAsync(
				article.Id, ArticleStatus.Classifying, cancellationToken);

			// Шаг 1 — генерируем embedding для статьи
			var embeddingText = $"{article.Title}. {article.Summary}";
			var embedding = await embeddingService.GenerateEmbeddingAsync(
				embeddingText, cancellationToken);

			// Шаг 2 — векторный поиск похожих событий
			var similarEvents = await eventRepository.FindSimilarEventsAsync(
				embedding,
				_options.AutoNewEventThreshold,
				_options.SimilarityWindowHours,
				cancellationToken);

			Event targetEvent;
			EventArticleRole role;

			if (similarEvents.Count == 0)
			{
				// Нет похожих — новое событие, summary не трогаем
				_logger.LogInformation(
					"No similar events found for article {Id}, creating new event", article.Id);
				targetEvent = await CreateNewEventAsync(
					article, embedding, eventRepository, cancellationToken);
				role = EventArticleRole.Initiator;
			}
			else
			{
				var (topEvent, topSimilarity) = similarEvents.First();

				if (topSimilarity >= _options.AutoSameEventThreshold)
				{
					// Высокое сходство — автоматически тот же Event
					_logger.LogInformation(
						"Article {Id} auto-matched to event {EventId} (similarity: {Similarity:F3})",
						article.Id, topEvent.Id, topSimilarity);
					targetEvent = topEvent;
					role = EventArticleRole.Update;

					// Обновляем summary используя summary статьи как новый факт
					await UpdateEventSummaryAsync(
						targetEvent,
						GetNewFactsForArticle(article),
						summaryUpdater,
						embeddingService,
						eventRepository,
						cancellationToken);
				}
				else
				{
					// Серая зона — спрашиваем Claude
					_logger.LogInformation(
						"Article {Id} in grey zone (top similarity: {Similarity:F3}), asking Claude",
						article.Id, topSimilarity);

					var candidates = similarEvents.Select(x => x.Event).ToList();
					var result = await classifier.ClassifyAsync(article, candidates, cancellationToken);

					_logger.LogInformation(
						"Claude classification for article {Id}: IsNewEvent={IsNew}, Reasoning={Reasoning}",
						article.Id, result.IsNewEvent, result.Reasoning);

					if (result.IsNewEvent || result.MatchedEventId is null)
					{
						targetEvent = await CreateNewEventAsync(
							article, embedding, eventRepository, cancellationToken);
						role = EventArticleRole.Initiator;
					}
					else
					{
						targetEvent = candidates.First(e => e.Id == result.MatchedEventId);
						role = result.Contradictions.Count > 0
							? EventArticleRole.Contradiction
							: EventArticleRole.Update;
					}

					// Сохраняем противоречия если есть
					if (result.Contradictions.Count > 0)
					{
						await SaveContradictionsAsync(
							result, targetEvent, article, eventRepository, cancellationToken);
					}

					// Сохраняем значимый апдейт и обновляем summary используя факты от Claude
					List<string> classifiedNewFacts = [];

					if (!result.IsNewEvent && result.IsSignificantUpdate && result.NewFacts.Count > 0)
					{
						classifiedNewFacts = result.NewFacts;
						await TrySaveEventUpdateAsync(
							targetEvent, article, result.NewFacts,
							eventRepository, cancellationToken);
					}

					// Обновляем summary если это не новое событие
					if (role != EventArticleRole.Initiator)
					{
						await UpdateEventSummaryAsync(
							targetEvent,
							classifiedNewFacts, // факты от Claude или пустой список
							summaryUpdater,
							embeddingService,
							eventRepository,
							cancellationToken);
					}
				}
			}

			// Привязываем статью к событию
			await eventRepository.AssignArticleToEventAsync(
				article.Id, targetEvent.Id, role, cancellationToken);

			// Переводим статью в следующий статус
			await articleRepository.UpdateStatusAsync(
				article.Id, ArticleStatus.AnalysisDone, cancellationToken);

			_logger.LogInformation(
				"Article {Id} classified as {Role} for event {EventId}",
				article.Id, role, targetEvent.Id);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to classify article {Id}", article.Id);

			await articleRepository.IncrementRetryAsync(article.Id, cancellationToken);
			var newRetryCount = article.RetryCount + 1;

			if (newRetryCount >= _options.MaxRetryCount)
			{
				_logger.LogWarning(
					"Article {Id} exceeded max retries for classification, moving to AnalysisDone anyway",
					article.Id);
				await articleRepository.UpdateStatusAsync(
					article.Id, ArticleStatus.AnalysisDone, cancellationToken);
			}
		}
	}

	private static List<string> GetNewFactsForArticle(Article article)
	{
		// Используем Summary статьи как новый факт
		return string.IsNullOrWhiteSpace(article.Summary)
			? []
			: [article.Summary];
	}

	private async Task UpdateEventSummaryAsync(
		Event evt,
		List<string> newFacts,
		IEventSummaryUpdater summaryUpdater,
		IGeminiEmbeddingService embeddingService,
		IEventRepository eventRepository,
		CancellationToken cancellationToken)
	{
		try
		{
			if (newFacts.Count == 0)
			{
				await eventRepository.UpdateLastUpdatedAtAsync(
					evt.Id, DateTimeOffset.UtcNow, cancellationToken);
				return;
			}

			// Обновляем summary через Claude
			var updatedSummary = await summaryUpdater.UpdateSummaryAsync(
				evt, newFacts, cancellationToken);

			// Перегенерируем embedding для нового summary
			var embeddingText = $"{evt.Title}. {updatedSummary}";
			var newEmbedding = await embeddingService.GenerateEmbeddingAsync(
				embeddingText, cancellationToken);

			// Сохраняем
			await eventRepository.UpdateSummaryAndEmbeddingAsync(
				evt.Id, updatedSummary, newEmbedding, cancellationToken);

			_logger.LogInformation(
				"Updated summary and embedding for event {EventId}", evt.Id);
		}
		catch (Exception ex)
		{
			// Не блокируем пайплайн если обновление summary упало
			_logger.LogError(ex,
				"Failed to update summary for event {EventId}, continuing", evt.Id);

			await eventRepository.UpdateLastUpdatedAtAsync(
				evt.Id, DateTimeOffset.UtcNow, cancellationToken);
		}
	}

	private static async Task<Event> CreateNewEventAsync(
		Article article,
		float[] embedding,
		IEventRepository eventRepository,
		CancellationToken cancellationToken)
	{
		var newEvent = new Event
		{
			Id = Guid.NewGuid(),
			Title = article.Title,
			Summary = article.Summary ?? string.Empty,
			Status = EventStatus.Active,
			FirstSeenAt = DateTimeOffset.UtcNow,
			LastUpdatedAt = DateTimeOffset.UtcNow,
			Embedding = embedding,
		};

		return await eventRepository.CreateAsync(newEvent, cancellationToken);
	}

	private async Task TrySaveEventUpdateAsync(
		Event targetEvent,
		Article article,
		List<string> newFacts,
		IEventRepository eventRepository,
		CancellationToken cancellationToken)
	{
		// Проверяем лимит апдейтов в сутки
		var todayCount = await eventRepository.CountTodayUpdatesAsync(
			targetEvent.Id, cancellationToken);

		if (todayCount >= _options.MaxUpdatesPerDay)
		{
			_logger.LogInformation(
				"Event {EventId} reached max updates per day ({Max}), skipping update",
				targetEvent.Id, _options.MaxUpdatesPerDay);
			return;
		}

		// Проверяем минимальный интервал между апдейтами
		var lastUpdateTime = await eventRepository.GetLastUpdateTimeAsync(
			targetEvent.Id, cancellationToken);

		if (lastUpdateTime.HasValue)
		{
			var minutesSinceLast = (DateTimeOffset.UtcNow - lastUpdateTime.Value).TotalMinutes;
			if (minutesSinceLast < _options.MinUpdateIntervalMinutes)
			{
				_logger.LogInformation(
					"Event {EventId} updated {Minutes:F0} min ago (min: {Min}), skipping update",
					targetEvent.Id, minutesSinceLast, _options.MinUpdateIntervalMinutes);
				return;
			}
		}

		var factSummary = string.Join(" ", newFacts);

		await eventRepository.AddEventUpdateAsync(new EventUpdate
		{
			Id = Guid.NewGuid(),
			EventId = targetEvent.Id,
			ArticleId = article.Id,
			FactSummary = factSummary,
			IsPublished = false,
			CreatedAt = DateTimeOffset.UtcNow,
		}, cancellationToken);

		_logger.LogInformation(
			"Saved significant update for event {EventId}: {Facts}",
			targetEvent.Id, factSummary);
	}

	private static async Task SaveContradictionsAsync(
		Core.DomainModels.AI.EventClassificationResult result,
		Event targetEvent,
		Article article,
		IEventRepository eventRepository,
		CancellationToken cancellationToken)
	{
		foreach (var contradiction in result.Contradictions)
		{
			var articleIds = contradiction.ArticleIds.Count > 0
				? contradiction.ArticleIds
				: [article.Id];

			// Убеждаемся что текущая статья всегда в списке
			if (!articleIds.Contains(article.Id))
				articleIds = [.. articleIds, article.Id];

			await eventRepository.AddContradictionAsync(
				new Contradiction
				{
					Id = Guid.NewGuid(),
					EventId = targetEvent.Id,
					Description = contradiction.Description,
					IsResolved = false,
					CreatedAt = DateTimeOffset.UtcNow,
				},
				articleIds,
				cancellationToken);
		}
	}
}