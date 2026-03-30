using Core.DomainModels;
using Core.Interfaces.Parsers;
using Core.Interfaces.Repositories;
using Core.Interfaces.Validators;
using Microsoft.Extensions.Options;
using Worker.Configuration;

namespace Worker.Workers;

public class SourceFetcherWorker : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<SourceFetcherWorker> _logger;
	private readonly RssFetcherOptions _options;

	public SourceFetcherWorker(
		IServiceScopeFactory scopeFactory,
		ILogger<SourceFetcherWorker> logger,
		IOptions<RssFetcherOptions> options)
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

		var saved = 0;
		var skipped = 0;

		foreach (var rawArticle in rawArticles)
		{
			if (string.IsNullOrEmpty(rawArticle.ExternalId)) continue;

			var (isValid, reason) = validator.Validate(rawArticle);
			if (!isValid)
			{
				_logger.LogDebug("Skipping article '{Title}' from {SourceName}: {Reason}",
					rawArticle.Title, source.Name, reason);
				skipped++;
				continue;
			}

			var exists = await rawArticleRepository.ExistsAsync(source.Id, rawArticle.ExternalId, cancellationToken);
			if (exists) continue;

			await rawArticleRepository.AddAsync(rawArticle, cancellationToken);
			saved++;
		}

		_logger.LogInformation("Saved {Saved} new articles from {SourceName}, skipped {Skipped} invalid",
			saved, source.Name, skipped);
	}
}
