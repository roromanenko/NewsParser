using Core.DomainModels;
using Core.Interfaces.AI;
using Core.Interfaces.Repositories;
using Infrastructure.AI.Telemetry;
using Microsoft.Extensions.Options;
using Worker.Configuration;

namespace Worker.Workers;

public class PublicationGenerationWorker : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<PublicationGenerationWorker> _logger;
	private readonly PublicationGenerationWorkerOptions _options;

	public PublicationGenerationWorker(
		IServiceScopeFactory scopeFactory,
		ILogger<PublicationGenerationWorker> logger,
		IOptions<PublicationGenerationWorkerOptions> options)
	{
		_scopeFactory = scopeFactory;
		_logger = logger;
		_options = options.Value;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			await ProcessBatchAsync(stoppingToken);
			await Task.Delay(
				TimeSpan.FromSeconds(_options.IntervalSeconds),
				stoppingToken);
		}
	}

	private async Task ProcessBatchAsync(CancellationToken cancellationToken)
	{
		var cycleId = Guid.NewGuid();
		using var cycleScope = _logger.BeginScope(new Dictionary<string, object>
		{
			["Worker"] = nameof(PublicationGenerationWorker),
			["CycleId"] = cycleId
		});

		using var scope = _scopeFactory.CreateScope();
		var publicationRepository = scope.ServiceProvider.GetRequiredService<IPublicationRepository>();
		var contentGenerator = scope.ServiceProvider.GetRequiredService<IContentGenerator>();

		var publications = await publicationRepository
			.GetPendingForGenerationAsync(_options.BatchSize, cancellationToken);

		if (publications.Count == 0)
			return;

		_logger.LogInformation("Found {Count} publications pending content generation", publications.Count);

		foreach (var publication in publications)
		{
			using var itemScope = _logger.BeginScope(new Dictionary<string, object>
			{
				["PublicationId"] = publication.Id,
				["EventId"] = publication.EventId,
				["ProjectId"] = publication.ProjectId
			});

			await publicationRepository.UpdateStatusAsync(
				publication.Id, PublicationStatus.GenerationInProgress, cancellationToken);

			await GenerateContentForPublicationAsync(
				publication, contentGenerator, publicationRepository, cycleId, cancellationToken);
		}
	}

	private async Task GenerateContentForPublicationAsync(
		Publication publication,
		IContentGenerator contentGenerator,
		IPublicationRepository publicationRepository,
		Guid cycleId,
		CancellationToken cancellationToken)
	{
		if (publication.Event is null)
		{
			_logger.LogWarning("Publication {Id} has no Event loaded, skipping content generation", publication.Id);
			await publicationRepository.UpdateStatusAsync(publication.Id, PublicationStatus.Failed, cancellationToken);
			return;
		}

		try
		{
			using var _ = AiCallContext.Push(cycleId, null, nameof(PublicationGenerationWorker));
			_logger.LogInformation("Generating content for publication {Id}, event {EventTitle}, target {Target}",
				publication.Id, publication.Event.Title, publication.PublishTarget.Name);

			var content = await contentGenerator.GenerateForPlatformAsync(
				publication.Event,
				publication.PublishTarget,
				cancellationToken,
				updateContext: publication.UpdateContext,
				editorFeedback: publication.EditorFeedback);

			await publicationRepository.UpdateGeneratedContentAsync(publication.Id, content, cancellationToken);
			await publicationRepository.UpdateStatusAsync(publication.Id, PublicationStatus.ContentReady, cancellationToken);

			_logger.LogInformation("Successfully generated content for publication {Id}", publication.Id);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to generate content for publication {Id}", publication.Id);
			await publicationRepository.UpdateStatusAsync(publication.Id, PublicationStatus.Failed, cancellationToken);
		}
	}
}
