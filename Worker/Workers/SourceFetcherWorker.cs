using Core.DomainModels;
using Core.Interfaces.Parsers;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Core.Interfaces.Validators;
using Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Worker.Configuration;

namespace Worker.Workers;

public class SourceFetcherWorker : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<SourceFetcherWorker> _logger;
	private readonly RssFetcherOptions _options;
	private readonly ValidationOptions _validationOptions;

	public SourceFetcherWorker(
		IServiceScopeFactory scopeFactory,
		ILogger<SourceFetcherWorker> logger,
		IOptions<RssFetcherOptions> options,
		IOptions<ValidationOptions> validationOptions)
	{
		_scopeFactory = scopeFactory;
		_logger = logger;
		_options = options.Value;
		_validationOptions = validationOptions.Value;
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
		var sourceRepository = scope.ServiceProvider.GetRequiredService<ISourceRepository>();
		var articleRepository = scope.ServiceProvider.GetRequiredService<IArticleRepository>();
		var validator = scope.ServiceProvider.GetRequiredService<IArticleValidator>();
		var parsers = scope.ServiceProvider.GetServices<ISourceParser>()
		                   .ToDictionary(p => p.SourceType);
		var mediaIngestionService = scope.ServiceProvider.GetRequiredService<IMediaIngestionService>();

		foreach (var (sourceType, parser) in parsers)
		{
			var sources = await sourceRepository.GetActiveAsync(sourceType, cancellationToken);
			_logger.LogInformation("Found {Count} active {Type} sources", sources.Count, sourceType);

			foreach (var source in sources)
			{
				try
				{
					await ProcessSourceAsync(source, parser, articleRepository, validator, mediaIngestionService, cancellationToken);
					await sourceRepository.UpdateLastFetchedAtAsync(source.Id, DateTimeOffset.UtcNow, cancellationToken);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed to process source {SourceName}", source.Name);
				}
			}
		}
	}

	private async Task ProcessSourceAsync(
		Source source,
		ISourceParser parser,
		IArticleRepository articleRepository,
		IArticleValidator validator,
		IMediaIngestionService mediaIngestionService,
		CancellationToken cancellationToken)
	{
		var articles = await parser.ParseAsync(source, cancellationToken);
		_logger.LogInformation("Parsed {Count} articles from {SourceName}", articles.Count, source.Name);

		// Fetch once per source — reused for every article in the batch
		var recentTitles = await articleRepository.GetRecentTitlesForDeduplicationAsync(
			_validationOptions.TitleDeduplicationWindowHours, cancellationToken);

		var saved = 0;
		var skipped = 0;

		foreach (var article in articles)
		{
			if (string.IsNullOrEmpty(article.ExternalId)) continue;

			var (isValid, reason) = validator.Validate(article);
			if (!isValid)
			{
				_logger.LogDebug("Skipping '{Title}' from {SourceName}: {Reason}", article.Title, source.Name, reason);
				skipped++;
				continue;
			}

			// ExternalId dedup
			var exists = await articleRepository.ExistsAsync(source.Id, article.ExternalId, cancellationToken);
			if (exists) continue;

			// URL deduplication
			var urlExists = await articleRepository.ExistsByUrlAsync(article.OriginalUrl ?? string.Empty, cancellationToken);
			if (urlExists)
			{
				_logger.LogDebug("Skipping '{Title}' — URL already exists: {Url}", article.Title, article.OriginalUrl);
				skipped++;
				continue;
			}

			// Title fuzzy deduplication
			if (recentTitles.Count > 0)
			{
				var bestScore = recentTitles
					.Max(t => FuzzySharp.Fuzz.TokenSetRatio(article.Title, t));

				if (bestScore >= _validationOptions.TitleSimilarityThreshold)
				{
					_logger.LogDebug("Skipping '{Title}' — title duplicate (score {Score})", article.Title, bestScore);
					skipped++;
					continue;
				}
			}

			await articleRepository.AddAsync(article, cancellationToken);

			try
			{
				await mediaIngestionService.IngestForArticleAsync(
					article.Id, article.MediaReferences, cancellationToken);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Media ingestion failed for article {ArticleId}", article.Id);
			}

			recentTitles.Add(article.Title); // intra-batch dedup
			saved++;
		}

		_logger.LogInformation("Saved {Saved} new articles from {SourceName}, skipped {Skipped}",
			saved, source.Name, skipped);
	}
}
