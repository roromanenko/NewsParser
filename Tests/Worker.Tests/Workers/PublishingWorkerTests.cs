using Core.DomainModels;
using Core.Interfaces.Publishers;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Worker.Configuration;
using Worker.Workers;

namespace Worker.Tests.Workers;

/// <summary>
/// Unit tests for <see cref="PublishingWorker"/> media resolution and dispatch logic.
/// All external dependencies are mocked; one short iteration is driven through
/// <c>StartAsync</c> / <c>StopAsync</c> with a long interval so the loop executes once.
/// </summary>
[TestFixture]
public class PublishingWorkerTests
{
    private Mock<IPublicationRepository> _publicationRepoMock = null!;
    private Mock<IMediaFileRepository> _mediaFileRepoMock = null!;
    private Mock<IPublisher> _publisherMock = null!;
    private Mock<IServiceScopeFactory> _scopeFactoryMock = null!;

    private IOptions<PublishingWorkerOptions> _workerOptions = null!;
    private IOptions<CloudflareR2Options> _r2Options = null!;

    [SetUp]
    public void SetUp()
    {
        _publicationRepoMock = new Mock<IPublicationRepository>();
        _mediaFileRepoMock = new Mock<IMediaFileRepository>();
        _publisherMock = new Mock<IPublisher>();

        _scopeFactoryMock = new Mock<IServiceScopeFactory>();

        // Very long interval so the worker loop only runs one iteration before cancellation.
        _workerOptions = Options.Create(new PublishingWorkerOptions
        {
            IntervalSeconds = 9999,
            BatchSize = 10,
        });

        _r2Options = Options.Create(new CloudflareR2Options
        {
            PublicBaseUrl = "https://cdn.example.com",
        });

        _publisherMock.Setup(p => p.Platform).Returns(Platform.Telegram);
        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<Publication>(), It.IsAny<List<ResolvedMedia>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("42");
        _publisherMock
            .Setup(p => p.PublishReplyAsync(It.IsAny<Publication>(), It.IsAny<string>(), It.IsAny<List<ResolvedMedia>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("43");
    }

    // ------------------------------------------------------------------
    // P0 — Empty SelectedMediaFileIds → PublishAsync called with empty media list
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessBatchAsync_WhenSelectedMediaFileIdsIsEmpty_PublishesWithEmptyMediaList()
    {
        // Arrange
        var publication = CreatePublication(selectedMediaFileIds: []);

        _publicationRepoMock
            .Setup(r => r.GetPendingForPublishAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([publication]);

        WireUpScopeFactory();

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _mediaFileRepoMock.Verify(
            r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _publisherMock.Verify(
            p => p.PublishAsync(
                publication,
                It.Is<List<ResolvedMedia>>(m => m.Count == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P0 — Non-empty SelectedMediaFileIds → GetByIdsAsync called; URL constructed as
    //       {PublicBaseUrl}/{R2Key}; resolved list passed to publisher
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessBatchAsync_WhenSelectedMediaFileIdsIsNonEmpty_ResolvesUrlsAndPublishes()
    {
        // Arrange
        var mediaId = Guid.NewGuid();
        const string r2Key = "articles/abc/photo.jpg";
        const string expectedUrl = "https://cdn.example.com/articles/abc/photo.jpg";

        var mediaFile = CreateMediaFile(mediaId, r2Key, sizeBytes: 1_024_000, kind: MediaKind.Image);
        var publication = CreatePublication(selectedMediaFileIds: [mediaId]);

        _publicationRepoMock
            .Setup(r => r.GetPendingForPublishAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([publication]);

        _mediaFileRepoMock
            .Setup(r => r.GetByIdsAsync(new List<Guid> { mediaId }, It.IsAny<CancellationToken>()))
            .ReturnsAsync([mediaFile]);

        WireUpScopeFactory();

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _mediaFileRepoMock.Verify(
            r => r.GetByIdsAsync(new List<Guid> { mediaId }, It.IsAny<CancellationToken>()),
            Times.Once);

        _publisherMock.Verify(
            p => p.PublishAsync(
                publication,
                It.Is<List<ResolvedMedia>>(m =>
                    m.Count == 1 &&
                    m[0].Url == expectedUrl &&
                    m[0].Kind == MediaKind.Image),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P1 — File exceeds 20 MB → excluded from resolved list
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessBatchAsync_WhenMediaFileExceedsMaxSize_ExcludesFileFromResolvedList()
    {
        // Arrange
        const long oversizedBytes = 21 * 1024 * 1024; // 21 MB > 20 MB limit

        var oversizedId = Guid.NewGuid();
        var oversizedFile = CreateMediaFile(oversizedId, "articles/big/file.jpg", sizeBytes: oversizedBytes, kind: MediaKind.Image);

        var publication = CreatePublication(selectedMediaFileIds: [oversizedId]);

        _publicationRepoMock
            .Setup(r => r.GetPendingForPublishAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([publication]);

        _mediaFileRepoMock
            .Setup(r => r.GetByIdsAsync(new List<Guid> { oversizedId }, It.IsAny<CancellationToken>()))
            .ReturnsAsync([oversizedFile]);

        WireUpScopeFactory();

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _publisherMock.Verify(
            p => p.PublishAsync(
                publication,
                It.Is<List<ResolvedMedia>>(m => m.Count == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P1 — ParentPublicationId is set → PublishReplyAsync called with parent message ID
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessBatchAsync_WhenParentPublicationIdIsSet_CallsPublishReplyAsync()
    {
        // Arrange
        var parentPublicationId = Guid.NewGuid();
        const string parentMessageId = "100";

        var publication = CreatePublication(
            selectedMediaFileIds: [],
            parentPublicationId: parentPublicationId);

        _publicationRepoMock
            .Setup(r => r.GetPendingForPublishAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([publication]);

        _publicationRepoMock
            .Setup(r => r.GetExternalMessageIdAsync(parentPublicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentMessageId);

        WireUpScopeFactory();

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _publisherMock.Verify(
            p => p.PublishReplyAsync(
                publication,
                parentMessageId,
                It.Is<List<ResolvedMedia>>(m => m.Count == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _publisherMock.Verify(
            p => p.PublishAsync(It.IsAny<Publication>(), It.IsAny<List<ResolvedMedia>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private PublishingWorker CreateWorker() =>
        new(
            _scopeFactoryMock.Object,
            NullLogger<PublishingWorker>.Instance,
            _workerOptions);

    /// <summary>
    /// Starts the worker, lets it process one full iteration, then stops it.
    /// The Task.Delay(9999 s) in the loop is cancelled by StopAsync.
    /// </summary>
    private static async Task RunOneIterationAsync(PublishingWorker sut)
    {
        using var cts = new CancellationTokenSource();

        await sut.StartAsync(cts.Token);
        await Task.Delay(300);
        await sut.StopAsync(CancellationToken.None);
    }

    private void WireUpScopeFactory()
    {
        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IPublicationRepository)))
            .Returns(_publicationRepoMock.Object);

        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IMediaFileRepository)))
            .Returns(_mediaFileRepoMock.Object);

        // GetServices<IPublisher>() is resolved via IEnumerable<IPublisher>
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IEnumerable<IPublisher>)))
            .Returns(new[] { _publisherMock.Object });

        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IOptions<CloudflareR2Options>)))
            .Returns(_r2Options);

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
    }

    private static Publication CreatePublication(
        List<Guid> selectedMediaFileIds,
        Guid? parentPublicationId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            GeneratedContent = "Test content",
            Status = PublicationStatus.Approved,
            SelectedMediaFileIds = selectedMediaFileIds,
            ParentPublicationId = parentPublicationId,
            PublishTarget = new PublishTarget
            {
                Id = Guid.NewGuid(),
                Name = "Test Channel",
                Platform = Platform.Telegram,
                Identifier = "-100123456789",
                IsActive = true,
            },
        };

    private static MediaFile CreateMediaFile(
        Guid id,
        string r2Key,
        long sizeBytes,
        MediaKind kind) =>
        new()
        {
            Id = id,
            ArticleId = Guid.NewGuid(),
            R2Key = r2Key,
            OriginalUrl = "https://origin.example.com/photo.jpg",
            ContentType = kind == MediaKind.Image ? "image/jpeg" : "video/mp4",
            SizeBytes = sizeBytes,
            Kind = kind,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
