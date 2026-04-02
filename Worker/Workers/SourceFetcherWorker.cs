using Core.DomainModels;
using Core.Interfaces.Parsers;
using Core.Interfaces.Repositories;
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
		var rawArticleRepository = scope.ServiceProvider.GetRequiredService<IRawArticleRepository>();
		var validator = scope.ServiceProvider.GetRequiredService<IRawArticleValidator>();
		var parsers = scope.ServiceProvider.GetServices<ISourceParser>()
		                   .ToDictionary(p => p.SourceType);

		foreach (var (sourceType, parser) in parsers)
		{
			var sources = await sourceRepository.GetActiveAsync(sourceType, cancellationToken);
			_logger.LogInformation("Found {Count} active {Type} sources", sources.Count, sourceType);

			foreach (var source in sources)
			{
				try
				{
					await ProcessSourceAsync(source, parser, rawArticleRepository, validator, cancellationToken);
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
		IRawArticleRepository rawArticleRepository,
		IRawArticleValidator validator,
		CancellationToken cancellationToken)
	{
		var rawArticles = await parser.ParseAsync(source, cancellationToken);
		_logger.LogInformation("Parsed {Count} articles from {SourceName}", rawArticles.Count, source.Name);

		// Fetch once per source — reused for every article in the batch
		var recentTitles = await rawArticleRepository.GetRecentTitlesForDeduplicationAsync(
			_validationOptions.TitleDeduplicationWindowHours, cancellationToken);

		var saved = 0;
		var skipped = 0;

		foreach (var rawArticle in rawArticles)
		{
			if (string.IsNullOrEmpty(rawArticle.ExternalId)) continue;

			var (isValid, reason) = validator.Validate(rawArticle);
			if (!isValid)
			{
				_logger.LogDebug("Skipping '{Title}' from {SourceName}: {Reason}", rawArticle.Title, source.Name, reason);
				skipped++;
				continue;
			}

			// ExternalId dedup (existing)
			var exists = await rawArticleRepository.ExistsAsync(source.Id, rawArticle.ExternalId, cancellationToken);
			if (exists) continue;

			// URL deduplication (cross-source, new)
			var urlExists = await rawArticleRepository.ExistsByUrlAsync(rawArticle.OriginalUrl, cancellationToken);
			if (urlExists)
			{
				_logger.LogDebug("Skipping '{Title}' — URL already exists: {Url}", rawArticle.Title, rawArticle.OriginalUrl);
				skipped++;
				continue;
			}

			// Title fuzzy deduplication (new)
			if (recentTitles.Count > 0)
			{
				var bestScore = recentTitles
					.Select(t => FuzzySharp.Fuzz.TokenSetRatio(rawArticle.Title, t))
					.Max();

				if (bestScore >= _validationOptions.TitleSimilarityThreshold)
				{
					_logger.LogDebug("Skipping '{Title}' — title duplicate (score {Score})", rawArticle.Title, bestScore);
					skipped++;
					continue;
				}
			}

			await rawArticleRepository.AddAsync(rawArticle, cancellationToken);
			recentTitles.Add(rawArticle.Title); // intra-batch dedup
			saved++;
		}

		_logger.LogInformation("Saved {Saved} new articles from {SourceName}, skipped {Skipped}",
			saved, source.Name, skipped);
	}
}
