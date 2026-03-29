using Core.DomainModels;
using Core.Interfaces.AI;
using Core.Interfaces.Repositories;
using Infrastructure.Configuration;
using Infrastructure.Persistence.Mappers;
using Microsoft.Extensions.Options;
using Worker.Configuration;

namespace Worker.Workers;

public class ArticleAnalysisWorker : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<ArticleAnalysisWorker> _logger;
	private readonly ArticleProcessingOptions _options;
	private readonly AiOptions _aiOptions;
	private readonly ValidationOptions _validationOptions;

	public ArticleAnalysisWorker(
		IServiceScopeFactory scopeFactory,
		ILogger<ArticleAnalysisWorker> logger,
		IOptions<ArticleProcessingOptions> options,
		IOptions<AiOptions> aiOptions,
		IOptions<ValidationOptions> validationOptions)
	{
		_scopeFactory = scopeFactory;
		_logger = logger;
		_options = options.Value;
		_aiOptions = aiOptions.Value;
		_validationOptions = validationOptions.Value;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			await ProcessAsync(stoppingToken);
			await Task.Delay(TimeSpan.FromSeconds(_options.AnalyzerIntervalSeconds), stoppingToken);
		}
	}

	private async Task ProcessAsync(CancellationToken cancellationToken)
	{
		using var scope = _scopeFactory.CreateScope();
		var articleRepository = scope.ServiceProvider.GetRequiredService<IArticleRepository>();
		var rawArticleRepository = scope.ServiceProvider.GetRequiredService<IRawArticleRepository>();
		var analyzer = scope.ServiceProvider.GetRequiredService<IArticleAnalyzer>();
		var embeddingService = scope.ServiceProvider.GetRequiredService<IGeminiEmbeddingService>();

		var rawArticles = await articleRepository.GetPendingForAnalysisAsync(_options.BatchSize, cancellationToken);

		if (rawArticles.Count == 0)
		{
			_logger.LogInformation("No raw articles pending for analysis");
			return;
		}

		_logger.LogInformation("Found {Count} raw articles for analysis", rawArticles.Count);

		foreach (var rawArticle in rawArticles)
		{
			await ProcessRawArticleAsync(
				rawArticle,
				analyzer,
				embeddingService,
				articleRepository,
				rawArticleRepository,
				cancellationToken);
		}
	}

	private async Task ProcessRawArticleAsync(
		RawArticle rawArticle,
		IArticleAnalyzer analyzer,
		IGeminiEmbeddingService embeddingService,
		IArticleRepository articleRepository,
		IRawArticleRepository rawArticleRepository,
		CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Analyzing raw article {Id}: {Title}", rawArticle.Id, rawArticle.Title);
			await articleRepository.UpdateRawArticleStatusAsync(rawArticle.Id, RawArticleStatus.Analyzing, cancellationToken);

			// Шаг 1 — быстрая дедупликация по заголовку до отправки в Claude
			var recentTitles = await rawArticleRepository.GetRecentTitlesAsync(
				rawArticle.Id,
				_validationOptions.TitleDeduplicationWindowHours,
				cancellationToken);

			if (recentTitles.Count > 0)
			{
				var bestScore = recentTitles
					.Select(t => FuzzySharp.Fuzz.TokenSetRatio(rawArticle.Title, t))
					.Max();

				if (bestScore >= _validationOptions.TitleSimilarityThreshold)
				{
					_logger.LogInformation(
						"Raw article {Id} is a title duplicate (score {Score}), rejecting. Title: {Title}",
						rawArticle.Id, bestScore, rawArticle.Title);
					await articleRepository.UpdateRawArticleStatusAsync(rawArticle.Id, RawArticleStatus.Rejected, cancellationToken);
					return;
				}
			}

			// Шаг 2 — анализ через Claude
			var result = await analyzer.AnalyzeAsync(rawArticle, cancellationToken);

			// Шаг 3 — генерация embedding для summary
			var embedding = await embeddingService.GenerateEmbeddingAsync(result.Summary, cancellationToken);

			// Шаг 4 — сохраняем embedding в RawArticle
			await rawArticleRepository.UpdateEmbeddingAsync(rawArticle.Id, embedding, cancellationToken);

			// Шаг 5 — проверка на дубликат
			var isDuplicate = await rawArticleRepository.HasSimilarAsync(
				rawArticle.Id,
				embedding,
				_aiOptions.Gemini.DeduplicationThreshold,
				_aiOptions.Gemini.DeduplicationWindowHours,
				cancellationToken);

			if (isDuplicate)
			{
				_logger.LogInformation(
					"Raw article {Id} is a duplicate, rejecting. Title: {Title}",
					rawArticle.Id, rawArticle.Title);
				await articleRepository.UpdateRawArticleStatusAsync(rawArticle.Id, RawArticleStatus.Rejected, cancellationToken);
				return;
			}

			// Шаг 6 — создаём Article
			var article = ArticleMapper.FromAnalysisResult(rawArticle, result, _aiOptions.Gemini.AnalyzerModel);
			await articleRepository.AddAsync(article, cancellationToken);
			await articleRepository.UpdateRawArticleStatusAsync(rawArticle.Id, RawArticleStatus.Completed, cancellationToken);

			_logger.LogInformation("Successfully analyzed raw article {Id}", rawArticle.Id);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to analyze raw article {Id}", rawArticle.Id);

			await articleRepository.IncrementRawArticleRetryAsync(rawArticle.Id, cancellationToken);

			var newRetryCount = rawArticle.RetryCount + 1;
			if (newRetryCount >= _options.MaxRetryCount)
			{
				_logger.LogWarning("Raw article {Id} exceeded max retries ({Max}), rejecting",
					rawArticle.Id, _options.MaxRetryCount);
				await articleRepository.UpdateRawArticleStatusAsync(rawArticle.Id, RawArticleStatus.Rejected, cancellationToken);
			}
			else
			{
				_logger.LogWarning("Raw article {Id} failed, retry {Current}/{Max}",
					rawArticle.Id, newRetryCount, _options.MaxRetryCount);
				await articleRepository.UpdateRawArticleStatusAsync(rawArticle.Id, RawArticleStatus.Pending, cancellationToken);
			}
		}
	}
}