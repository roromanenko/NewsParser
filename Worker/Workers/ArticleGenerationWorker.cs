using Core.DomainModels;
using Core.Interfaces.AI;
using Core.Interfaces.Repositories;
using Core.DomainModels.AI;
using Microsoft.Extensions.Options;
using Worker.Configuration;

namespace Worker.Workers;

public class ArticleGenerationWorker : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<ArticleGenerationWorker> _logger;
	private readonly ArticleProcessingOptions _options;

	public ArticleGenerationWorker(
		IServiceScopeFactory scopeFactory,
		ILogger<ArticleGenerationWorker> logger,
		IOptions<ArticleProcessingOptions> options)
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
			await Task.Delay(TimeSpan.FromSeconds(_options.GeneratorIntervalSeconds), stoppingToken);
		}
	}

	private async Task ProcessAsync(CancellationToken cancellationToken)
	{
		using var scope = _scopeFactory.CreateScope();
		var articleRepository = scope.ServiceProvider.GetRequiredService<IArticleRepository>();
		var generator = scope.ServiceProvider.GetRequiredService<IArticleGenerator>();

		var articles = await articleRepository.GetPendingForGenerationAsync(_options.BatchSize, cancellationToken);

		if (articles.Count == 0)
		{
			_logger.LogInformation("No articles pending for generation");
			return;
		}

		_logger.LogInformation("Found {Count} articles for generation", articles.Count);

		foreach (var article in articles)
		{
			await ProcessArticleAsync(article, generator, articleRepository, cancellationToken);
		}
	}

	private async Task ProcessArticleAsync(
		Article article,
		IArticleGenerator generator,
		IArticleRepository articleRepository,
		CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Generating content for article {Id}: {Summary}", article.Id, article.Summary);
			await articleRepository.UpdateStatusAsync(article.Id, ArticleStatus.Processing, cancellationToken);

			var analysis = new ArticleAnalysisResult
			{
				Category = article.Category,
				Tags = article.Tags,
				Sentiment = article.Sentiment.ToString(),
				Language = article.Language,
				Summary = article.Summary ?? string.Empty
			};

			var result = await generator.GenerateAsync(article.RawArticle, analysis, cancellationToken);

			await articleRepository.UpdateGeneratedContentAsync(
				article.Id,
				result.Title,
				result.Content,
				ArticleStatus.Pending,
				cancellationToken);

			_logger.LogInformation("Successfully generated content for article {Id}", article.Id);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to generate content for article {Id}", article.Id);

			await articleRepository.IncrementRetryAsync(article.Id, cancellationToken);

			var newRetryCount = article.RetryCount + 1;
			if (newRetryCount >= _options.MaxRetryCount)
			{
				_logger.LogWarning("Article {Id} exceeded max retries ({Max}), rejecting", article.Id, _options.MaxRetryCount);
				await articleRepository.UpdateStatusAsync(article.Id, ArticleStatus.Rejected, cancellationToken);
			}
			else
			{
				_logger.LogWarning("Article {Id} failed, retry {Current}/{Max}", article.Id, newRetryCount, _options.MaxRetryCount);
				await articleRepository.UpdateStatusAsync(article.Id, ArticleStatus.AnalysisDone, cancellationToken);
			}
		}
	}
}