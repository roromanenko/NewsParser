using Core.DomainModels;
using Core.Interfaces.Publishers;
using Core.Interfaces.Repositories;
using Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Worker.Configuration;

namespace Worker.Workers;

public class PublishingWorker : BackgroundService
{
	// Mirrors the Telegram 20 MB photo limit — kept local because this is a worker concern.
	private const long MaxMediaFileSizeBytes = 20 * 1024 * 1024;

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<PublishingWorker> _logger;
	private readonly PublishingWorkerOptions _options;

	public PublishingWorker(
		IServiceScopeFactory scopeFactory,
		ILogger<PublishingWorker> logger,
		IOptions<PublishingWorkerOptions> options)
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
		using var scope = _scopeFactory.CreateScope();
		var publicationRepository = scope.ServiceProvider.GetRequiredService<IPublicationRepository>();
		var publishers = scope.ServiceProvider.GetServices<IPublisher>().ToList();
		var mediaFileRepository = scope.ServiceProvider.GetRequiredService<IMediaFileRepository>();
		var r2Options = scope.ServiceProvider.GetRequiredService<IOptions<CloudflareR2Options>>().Value;

		var publications = await publicationRepository
			.GetPendingForPublishAsync(_options.BatchSize, cancellationToken);

		if (publications.Count == 0)
			return;

		_logger.LogInformation("Found {Count} publications ready for publishing", publications.Count);

		foreach (var publication in publications)
		{
			await PublishSingleAsync(
				publication, publishers, publicationRepository,
				mediaFileRepository, r2Options, cancellationToken);
		}
	}

	private async Task PublishSingleAsync(
		Publication publication,
		List<IPublisher> publishers,
		IPublicationRepository publicationRepository,
		IMediaFileRepository mediaFileRepository,
		CloudflareR2Options r2Options,
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

			var resolvedMedia = await ResolveMediaAsync(
				publication, mediaFileRepository, r2Options, cancellationToken);

			var externalMessageId = await ResolveAndPublishAsync(
				publication, publisher, resolvedMedia, publicationRepository, cancellationToken);

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

	private async Task<List<ResolvedMedia>> ResolveMediaAsync(
		Publication publication,
		IMediaFileRepository mediaFileRepository,
		CloudflareR2Options r2Options,
		CancellationToken cancellationToken)
	{
		if (publication.SelectedMediaFileIds.Count == 0)
			return [];

		var mediaFiles = await mediaFileRepository.GetByIdsAsync(
			publication.SelectedMediaFileIds, cancellationToken);

		var resolvedMedia = new List<ResolvedMedia>();

		foreach (var mediaFile in mediaFiles)
		{
			if (mediaFile.SizeBytes > MaxMediaFileSizeBytes)
			{
				_logger.LogWarning(
					"Skipping media file {FileId} ({SizeBytes} bytes) — exceeds the {Limit} byte limit",
					mediaFile.Id, mediaFile.SizeBytes, MaxMediaFileSizeBytes);
				continue;
			}

			var url = $"{r2Options.PublicBaseUrl.TrimEnd('/')}/{mediaFile.R2Key.TrimStart('/')}";
			resolvedMedia.Add(new ResolvedMedia(url, mediaFile.ContentType, mediaFile.Kind));
		}

		return resolvedMedia;
	}

	private async Task<string> ResolveAndPublishAsync(
		Publication publication,
		IPublisher publisher,
		List<ResolvedMedia> resolvedMedia,
		IPublicationRepository publicationRepository,
		CancellationToken cancellationToken)
	{
		if (!publication.ParentPublicationId.HasValue)
			return await publisher.PublishAsync(publication, resolvedMedia, cancellationToken);

		var parentMessageId = await publicationRepository.GetExternalMessageIdAsync(
			publication.ParentPublicationId.Value, cancellationToken);

		if (parentMessageId is null)
		{
			_logger.LogWarning("Parent publication {ParentId} has no ExternalMessageId yet, skipping {Id}",
				publication.ParentPublicationId.Value, publication.Id);
			throw new InvalidOperationException(
				$"Parent publication {publication.ParentPublicationId.Value} has no ExternalMessageId");
		}

		return await publisher.PublishReplyAsync(publication, parentMessageId, resolvedMedia, cancellationToken);
	}
}
