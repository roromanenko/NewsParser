using Core.DomainModels;
using Core.Interfaces.AI;
using Core.Interfaces.Repositories;
using Microsoft.Extensions.Options;
using Worker.Configuration;

namespace Worker.Workers;

public class PublicationGenerationWorker : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<PublicationGenerationWorker> _logger;
	private readonly ArticleProcessingOptions _options;

	public PublicationGenerationWorker(
		IServiceScopeFactory scopeFactory,
		ILogger<PublicationGenerationWorker> logger,
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
			await Task.Delay(
				TimeSpan.FromSeconds(_options.PublicationGenerationIntervalSeconds),
				stoppingToken);
		}
	}

	private async Task ProcessAsync(CancellationToken cancellationToken)
	{
		using var scope = _scopeFactory.CreateScope();

		var publicationRepository = scope.ServiceProvider
			.GetRequiredService<IPublicationRepository>();
		var contentGenerator = scope.ServiceProvider
			.GetRequiredService<IContentGenerator>();

		var publications = await publicationRepository
			.GetPendingForContentGenerationAsync(_options.BatchSize, cancellationToken);

		if (publications.Count == 0)
		{
			_logger.LogInformation("No publications pending for content generation");
			return;
		}

		_logger.LogInformation(
			"Found {Count} publications for content generation", publications.Count);

		foreach (var publication in publications)
		{
			await ProcessPublicationAsync(
				publication,
				contentGenerator,
				publicationRepository,
				cancellationToken);
		}
	}

	private async Task ProcessPublicationAsync(
		Publication publication,
		IContentGenerator contentGenerator,
		IPublicationRepository publicationRepository,
		CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation(
				"Generating content for publication {Id}, target {Target}",
				publication.Id, publication.PublishTarget.Name);

			var content = await contentGenerator.GenerateForPlatformAsync(
				publication.Article,
				publication.PublishTarget,
				cancellationToken,
				updateContext: publication.UpdateContext);

			await publicationRepository.UpdateGeneratedContentAsync(
				publication.Id,
				content,
				cancellationToken);

			await publicationRepository.UpdateStatusAsync(
				publication.Id,
				PublicationStatus.ContentReady,
				cancellationToken);

			_logger.LogInformation(
				"Successfully generated content for publication {Id}", publication.Id);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex,
				"Failed to generate content for publication {Id}, target {Target}",
				publication.Id, publication.PublishTarget.Name);
		}
	}
}