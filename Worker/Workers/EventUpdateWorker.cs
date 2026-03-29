using Core.DomainModels;
using Core.Interfaces.Repositories;
using Microsoft.Extensions.Options;
using Worker.Configuration;

namespace Worker.Workers;

public class EventUpdateWorker : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<EventUpdateWorker> _logger;
	private readonly ArticleProcessingOptions _options;

	public EventUpdateWorker(
		IServiceScopeFactory scopeFactory,
		ILogger<EventUpdateWorker> logger,
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
		var eventRepository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
		var publicationRepository = scope.ServiceProvider.GetRequiredService<IPublicationRepository>();

		var unpublishedUpdates = await eventRepository.GetUnpublishedUpdatesAsync(
			_options.BatchSize, cancellationToken);

		if (unpublishedUpdates.Count == 0)
		{
			_logger.LogInformation("No unpublished event updates");
			return;
		}

		_logger.LogInformation(
			"Found {Count} unpublished event updates", unpublishedUpdates.Count);

		foreach (var update in unpublishedUpdates)
		{
			await ProcessUpdateAsync(
				update, eventRepository, publicationRepository, cancellationToken);
		}
	}

	private async Task ProcessUpdateAsync(
		EventUpdate update,
		IEventRepository eventRepository,
		IPublicationRepository publicationRepository,
		CancellationToken cancellationToken)
	{
		try
		{
			// Ищем оригинальную публикацию события по тому же EventId и PublishTarget
			var originalPublication = await publicationRepository
				.GetOriginalEventPublicationAsync(update.EventId, cancellationToken);

			if (originalPublication is null)
			{
				_logger.LogWarning(
					"No original publication found for event {EventId}, skipping update {UpdateId}",
					update.EventId, update.Id);
				return;
			}

			// Создаём Publication как reply на оригинальную публикацию
			var publication = new Publication
			{
				Id = Guid.NewGuid(),
				PublishTargetId = originalPublication.PublishTargetId,
				GeneratedContent = string.Empty,
				Status = PublicationStatus.Pending,
				CreatedAt = DateTimeOffset.UtcNow,
				EventId = update.EventId,
				ParentPublicationId = originalPublication.Id,
				UpdateContext = update.FactSummary,
			};

			await publicationRepository.AddEventUpdatePublicationAsync(
				publication,
				update.Article.Id,
				cancellationToken);

			await eventRepository.MarkUpdatePublishedAsync(update.Id, cancellationToken);

			_logger.LogInformation(
				"Created reply publication {PubId} for event update {UpdateId}",
				publication.Id, update.Id);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex,
				"Failed to process event update {UpdateId}", update.Id);
		}
	}
}