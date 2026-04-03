using Core.DomainModels;
using Core.Interfaces.AI;
using Core.Interfaces.Publishers;
using Core.Interfaces.Repositories;
using Microsoft.Extensions.Options;
using Worker.Configuration;

namespace Worker.Workers;

public class PublicationWorker : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<PublicationWorker> _logger;
	private readonly ArticleProcessingOptions _options;

	public PublicationWorker(
		IServiceScopeFactory scopeFactory,
		ILogger<PublicationWorker> logger,
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
				TimeSpan.FromSeconds(_options.PublicationWorkerIntervalSeconds),
				stoppingToken);
		}
	}

	private async Task ProcessAsync(CancellationToken cancellationToken)
	{
		using var scope = _scopeFactory.CreateScope();
		var publicationRepository = scope.ServiceProvider.GetRequiredService<IPublicationRepository>();
		var contentGenerator = scope.ServiceProvider.GetRequiredService<IContentGenerator>();
		var publishers = scope.ServiceProvider.GetServices<IPublisher>().ToList();

		// Phase A — generate content for Pending publications
		await GenerateContentAsync(publicationRepository, contentGenerator, cancellationToken);

		// Phase B — publish ContentReady publications
		await PublishReadyAsync(publicationRepository, publishers, cancellationToken);
	}

	private async Task GenerateContentAsync(
		IPublicationRepository publicationRepository,
		IContentGenerator contentGenerator,
		CancellationToken cancellationToken)
	{
		var publications = await publicationRepository
			.GetPendingForContentGenerationAsync(_options.BatchSize, cancellationToken);

		if (publications.Count == 0) return;

		_logger.LogInformation("Found {Count} publications pending content generation", publications.Count);

		foreach (var publication in publications)
		{
			try
			{
				_logger.LogInformation("Generating content for publication {Id}, target {Target}",
					publication.Id, publication.PublishTarget.Name);

				var content = await contentGenerator.GenerateForPlatformAsync(
					publication.Article,
					publication.PublishTarget,
					cancellationToken,
					updateContext: publication.UpdateContext);

				await publicationRepository.UpdateGeneratedContentAsync(publication.Id, content, cancellationToken);
				await publicationRepository.UpdateStatusAsync(publication.Id, PublicationStatus.ContentReady, cancellationToken);

				_logger.LogInformation("Successfully generated content for publication {Id}", publication.Id);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to generate content for publication {Id}", publication.Id);
			}
		}
	}

	private async Task PublishReadyAsync(
		IPublicationRepository publicationRepository,
		List<IPublisher> publishers,
		CancellationToken cancellationToken)
	{
		var publications = await publicationRepository
			.GetReadyForPublishAsync(_options.BatchSize, cancellationToken);

		if (publications.Count == 0)
		{
			_logger.LogInformation("No publications ready for publishing");
			return;
		}

		_logger.LogInformation("Found {Count} publications ready for publishing", publications.Count);

		foreach (var publication in publications)
		{
			await ProcessPublicationAsync(publication, publishers, publicationRepository, cancellationToken);
		}
	}

	private async Task ProcessPublicationAsync(
		Publication publication,
		List<IPublisher> publishers,
		IPublicationRepository publicationRepository,
		CancellationToken cancellationToken)
	{
		var publisher = publishers.FirstOrDefault(p => p.Platform == publication.Platform);
		if (publisher is null)
		{
			_logger.LogWarning("No publisher found for platform {Platform}, publication {Id}",
				publication.Platform, publication.Id);
			return;
		}

		try
		{
			_logger.LogInformation("Publishing {Id} to {Target} ({Platform})",
				publication.Id, publication.PublishTarget.Name, publication.Platform);

			string externalMessageId;

			if (publication.ParentPublicationId.HasValue)
			{
				var parentMessageId = await publicationRepository.GetExternalMessageIdAsync(
					publication.ParentPublicationId.Value, cancellationToken);

				if (parentMessageId is null)
				{
					_logger.LogWarning("Parent publication {ParentId} has no ExternalMessageId yet, skipping {Id}",
						publication.ParentPublicationId.Value, publication.Id);
					return;
				}

				externalMessageId = await publisher.PublishReplyAsync(publication, parentMessageId, cancellationToken);
			}
			else
			{
				externalMessageId = await publisher.PublishAsync(publication, cancellationToken);
			}

			await publicationRepository.AddPublishLogAsync(new PublishLog
			{
				Id = Guid.NewGuid(),
				PublicationId = publication.Id,
				Status = PublishLogStatus.Success,
				AttemptedAt = DateTimeOffset.UtcNow,
				ExternalMessageId = externalMessageId,
			}, cancellationToken);

			await publicationRepository.UpdateStatusAsync(publication.Id, PublicationStatus.Published, cancellationToken);
			await publicationRepository.UpdatePublishedAtAsync(publication.Id, DateTimeOffset.UtcNow, cancellationToken);

			_logger.LogInformation("Successfully published {Id} to {Target}, message_id: {MessageId}",
				publication.Id, publication.PublishTarget.Name, externalMessageId);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to publish {Id} to {Target}", publication.Id, publication.PublishTarget.Name);

			await publicationRepository.AddPublishLogAsync(new PublishLog
			{
				Id = Guid.NewGuid(),
				PublicationId = publication.Id,
				Status = PublishLogStatus.Failed,
				ErrorMessage = ex.Message,
				AttemptedAt = DateTimeOffset.UtcNow,
			}, cancellationToken);

			await publicationRepository.UpdateStatusAsync(publication.Id, PublicationStatus.Failed, cancellationToken);
		}
	}
}
