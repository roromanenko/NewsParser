using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using Core.Interfaces.Repositories;
using Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Worker.Configuration;

namespace Worker.Workers;

public class ArticleAnalysisWorker : BackgroundService
{
	private record AnalysisContext(
		IArticleRepository ArticleRepository,
		IEventRepository EventRepository,
		IArticleAnalyzer Analyzer,
		IGeminiEmbeddingService EmbeddingService,
		IEventClassifier Classifier,
		IEventSummaryUpdater SummaryUpdater,
		IKeyFactsExtractor KeyFactsExtractor,
		IContradictionDetector ContradictionDetector);
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<ArticleAnalysisWorker> _logger;
	private readonly ArticleProcessingOptions _options;
	private readonly AiOptions _aiOptions;

	public ArticleAnalysisWorker(
		IServiceScopeFactory scopeFactory,
		ILogger<ArticleAnalysisWorker> logger,
		IOptions<ArticleProcessingOptions> options,
		IOptions<AiOptions> aiOptions)
	{
		_scopeFactory = scopeFactory;
		_logger = logger;
		_options = options.Value;
		_aiOptions = aiOptions.Value;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			await ProcessAsync(stoppingToken);
			await Task.Delay(TimeSpan.FromSeconds(_options.AnalysisIntervalSeconds), stoppingToken);
		}
	}

	private async Task ProcessAsync(CancellationToken cancellationToken)
	{
		using var scope = _scopeFactory.CreateScope();
		var ctx = new AnalysisContext(
			ArticleRepository: scope.ServiceProvider.GetRequiredService<IArticleRepository>(),
			EventRepository: scope.ServiceProvider.GetRequiredService<IEventRepository>(),
			Analyzer: scope.ServiceProvider.GetRequiredService<IArticleAnalyzer>(),
			EmbeddingService: scope.ServiceProvider.GetRequiredService<IGeminiEmbeddingService>(),
			Classifier: scope.ServiceProvider.GetRequiredService<IEventClassifier>(),
			SummaryUpdater: scope.ServiceProvider.GetRequiredService<IEventSummaryUpdater>(),
			KeyFactsExtractor: scope.ServiceProvider.GetRequiredService<IKeyFactsExtractor>(),
			ContradictionDetector: scope.ServiceProvider.GetRequiredService<IContradictionDetector>());

		var articles = await ctx.ArticleRepository.GetPendingAsync(_options.BatchSize, cancellationToken);

		if (articles.Count == 0)
		{
			_logger.LogInformation("No articles pending for analysis");
			return;
		}

		_logger.LogInformation("Found {Count} articles for analysis", articles.Count);

		foreach (var article in articles)
		{
			await ProcessArticleAsync(article, ctx, cancellationToken);
		}
	}

	private async Task ProcessArticleAsync(
		Article article,
		AnalysisContext ctx,
		CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Analyzing article {Id}: {Title}", article.Id, article.Title);
			await ctx.ArticleRepository.UpdateStatusAsync(article.Id, ArticleStatus.Analyzing, cancellationToken);

			var analysis = await ctx.Analyzer.AnalyzeAsync(article, cancellationToken);

			await ctx.ArticleRepository.UpdateAnalysisResultAsync(
				article.Id,
				analysis.Category,
				analysis.Tags,
				analysis.Sentiment,
				analysis.Language,
				analysis.Summary,
				_aiOptions.Gemini.AnalyzerModel,
				ArticleStatus.AnalysisDone,
				cancellationToken);

			article.Category = analysis.Category;
			article.Tags = analysis.Tags;
			article.Sentiment = Enum.Parse<Sentiment>(analysis.Sentiment);
			article.Language = analysis.Language;
			article.Summary = analysis.Summary;

			await ExtractAndPersistKeyFactsAsync(article, ctx, cancellationToken);

			var embeddingText = $"{article.Title}. {analysis.Summary}";
			var embedding = await ctx.EmbeddingService.GenerateEmbeddingAsync(embeddingText, cancellationToken);

			var isDuplicate = await ctx.ArticleRepository.HasSimilarAsync(
				article.Id,
				embedding,
				_options.DeduplicationThreshold,
				_options.DeduplicationWindowHours,
				cancellationToken);

			if (isDuplicate)
			{
				_logger.LogInformation("Article {Id} is a near-duplicate, rejecting", article.Id);
				await ctx.ArticleRepository.RejectAsync(article.Id, "duplicate_by_vector", cancellationToken);
				return;
			}

			await ctx.ArticleRepository.UpdateEmbeddingAsync(article.Id, embedding, cancellationToken);
			article.Embedding = embedding;

			await ClassifyIntoEventAsync(article, ctx, cancellationToken);

			_logger.LogInformation("Successfully processed article {Id}", article.Id);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to process article {Id}", article.Id);

			await ctx.ArticleRepository.IncrementRetryAsync(article.Id, cancellationToken);
			var newRetryCount = article.RetryCount + 1;

			if (newRetryCount >= _options.MaxRetryCount)
			{
				_logger.LogWarning("Article {Id} exceeded max retries, rejecting", article.Id);
				await ctx.ArticleRepository.UpdateStatusAsync(article.Id, ArticleStatus.Rejected, cancellationToken);
			}
			else
			{
				await ctx.ArticleRepository.UpdateStatusAsync(article.Id, ArticleStatus.Pending, cancellationToken);
			}
		}
	}

	private async Task ExtractAndPersistKeyFactsAsync(
		Article article,
		AnalysisContext ctx,
		CancellationToken cancellationToken)
	{
		try
		{
			var keyFacts = await ctx.KeyFactsExtractor.ExtractAsync(article, cancellationToken);
			await ctx.ArticleRepository.UpdateKeyFactsAsync(article.Id, keyFacts, cancellationToken);
			article.KeyFacts = keyFacts;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Key facts extraction failed for article {Id}, continuing", article.Id);
		}
	}

	private async Task ClassifyIntoEventAsync(
		Article article,
		AnalysisContext ctx,
		CancellationToken cancellationToken)
	{
		var embedding = article.Embedding!;

		var similarEvents = await ctx.EventRepository.FindSimilarEventsAsync(
			embedding,
			_options.AutoNewEventThreshold,
			_options.SimilarityWindowHours,
			cancellationToken);

		Event targetEvent;
		ArticleRole role;

		if (similarEvents.Count == 0)
		{
			_logger.LogInformation("No similar events for article {Id}, creating new event", article.Id);
			targetEvent = await CreateNewEventAsync(article, embedding, ctx.EventRepository, cancellationToken);
			role = ArticleRole.Initiator;
		}
		else
		{
			var (topEvent, topSimilarity) = similarEvents.First();

			if (topSimilarity >= _options.AutoSameEventThreshold)
			{
				_logger.LogInformation("Article {Id} auto-matched to event {EventId} (similarity: {S:F3})",
					article.Id, topEvent.Id, topSimilarity);
				targetEvent = topEvent;

				var contradictions = await ctx.ContradictionDetector.DetectAsync(article, targetEvent, cancellationToken);
				role = contradictions.Count > 0 ? ArticleRole.Contradiction : ArticleRole.Update;

				if (contradictions.Count > 0)
					await SaveContradictionsAsync(contradictions, targetEvent, article, ctx.EventRepository, cancellationToken);

				await UpdateEventEmbeddingAsync(
					targetEvent, article.Summary ?? string.Empty, embedding, ctx, cancellationToken);
			}
			else
			{
				_logger.LogInformation("Article {Id} in grey zone (top similarity: {S:F3}), asking classifier",
					article.Id, topSimilarity);

				var candidates = similarEvents.Select(x => x.Event).ToList();
				var result = await ctx.Classifier.ClassifyAsync(article, candidates, cancellationToken);

				if (result.IsNewEvent || result.MatchedEventId is null)
				{
					targetEvent = await CreateNewEventAsync(article, embedding, ctx.EventRepository, cancellationToken);
					role = ArticleRole.Initiator;
				}
				else
				{
					targetEvent = candidates.First(e => e.Id == result.MatchedEventId);
					role = result.Contradictions.Count > 0 ? ArticleRole.Contradiction : ArticleRole.Update;
				}

				if (result.Contradictions.Count > 0 && !result.IsNewEvent)
				{
					await SaveContradictionsAsync(result.Contradictions, targetEvent, article, ctx.EventRepository, cancellationToken);
				}

				List<string> newFacts = [];

				if (!result.IsNewEvent && result.IsSignificantUpdate && result.NewFacts.Count > 0)
				{
					newFacts = result.NewFacts;
					await TrySaveEventUpdateAsync(targetEvent, article, newFacts, ctx.EventRepository, cancellationToken);
				}

				if (role != ArticleRole.Initiator)
				{
					await UpdateEventEmbeddingAsync(
						targetEvent, article.Summary ?? string.Empty, embedding, ctx, cancellationToken);
				}
			}
		}

		await ctx.EventRepository.AssignArticleToEventAsync(article.Id, targetEvent.Id, role, cancellationToken);
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
			ArticleCount = 1,
		};

		return await eventRepository.CreateAsync(newEvent, cancellationToken);
	}

	private async Task UpdateEventEmbeddingAsync(
		Event evt,
		string newFact,
		float[] articleEmbedding,
		AnalysisContext ctx,
		CancellationToken cancellationToken)
	{
		try
		{
			var count = evt.ArticleCount > 0 ? evt.ArticleCount : 1;
			float[] updatedEmbedding;

			if (evt.Embedding != null)
			{
				updatedEmbedding = evt.Embedding
					.Zip(articleEmbedding, (old, @new) => (old * count + @new) / (count + 1))
					.ToArray();
			}
			else
			{
				updatedEmbedding = articleEmbedding;
			}

			var newFacts = string.IsNullOrWhiteSpace(newFact) ? [] : new List<string> { newFact };
			string updatedSummary = evt.Summary;

			if (newFacts.Count > 0)
			{
				updatedSummary = await ctx.SummaryUpdater.UpdateSummaryAsync(evt, newFacts, cancellationToken);
			}

			await ctx.EventRepository.UpdateSummaryAndEmbeddingAsync(
				evt.Id, updatedSummary, updatedEmbedding, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to update embedding for event {EventId}, continuing", evt.Id);
			await ctx.EventRepository.UpdateLastUpdatedAtAsync(evt.Id, DateTimeOffset.UtcNow, cancellationToken);
		}
	}

	private async Task TrySaveEventUpdateAsync(
		Event targetEvent,
		Article article,
		List<string> newFacts,
		IEventRepository eventRepository,
		CancellationToken cancellationToken)
	{
		var todayCount = await eventRepository.CountTodayUpdatesAsync(targetEvent.Id, cancellationToken);
		if (todayCount >= _options.MaxUpdatesPerDay) return;

		var lastUpdateTime = await eventRepository.GetLastUpdateTimeAsync(targetEvent.Id, cancellationToken);
		if (lastUpdateTime.HasValue)
		{
			var minutesSinceLast = (DateTimeOffset.UtcNow - lastUpdateTime.Value).TotalMinutes;
			if (minutesSinceLast < _options.MinUpdateIntervalMinutes) return;
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

		_logger.LogInformation("Saved significant update for event {EventId}: {Facts}", targetEvent.Id, factSummary);
	}

	private static async Task SaveContradictionsAsync(
		List<ContradictionInput> contradictions,
		Event targetEvent,
		Article article,
		IEventRepository eventRepository,
		CancellationToken cancellationToken)
	{
		foreach (var contradiction in contradictions)
		{
			var articleIds = contradiction.ArticleIds.Count > 0
				? contradiction.ArticleIds
				: [article.Id];

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
