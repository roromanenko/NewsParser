using Core.DomainModels;
using Core.Interfaces.AI;
using Core.Interfaces.Publishers;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Worker.Configuration;
using Worker.Workers;

namespace Worker.Tests.Workers;

[TestFixture]
public class PublicationWorkerTests
{
    private Mock<IServiceScopeFactory> _scopeFactoryMock = null!;
    private Mock<IPublicationRepository> _publicationRepoMock = null!;
    private Mock<IContentGenerator> _contentGeneratorMock = null!;
    private Mock<IPublisher> _publisherMock = null!;

    private IOptions<ArticleProcessingOptions> _options = null!;

    [SetUp]
    public void SetUp()
    {
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _publicationRepoMock = new Mock<IPublicationRepository>();
        _contentGeneratorMock = new Mock<IContentGenerator>();
        _publisherMock = new Mock<IPublisher>();

        _options = Options.Create(new ArticleProcessingOptions
        {
            PublicationWorkerIntervalSeconds = 9999,
            BatchSize = 10
        });

        _publisherMock.Setup(p => p.Platform).Returns(Platform.Telegram);

        SetupDefaultRepositoryBehaviours();
        WireUpScopeFactory();
    }

    // ------------------------------------------------------------------
    // P0 — Content generation is called with the Event argument (not Article)
    // ------------------------------------------------------------------

    [Test]
    public async Task GenerateContentAsync_WhenPublicationHasEvent_CallsGeneratorWithEvent()
    {
        // Arrange
        var evt = CreateEvent();
        var publication = CreatePublication(withEvent: evt);

        _publicationRepoMock
            .Setup(r => r.GetPendingForContentGenerationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([publication]);

        _contentGeneratorMock
            .Setup(g => g.GenerateForPlatformAsync(
                It.IsAny<Event>(),
                It.IsAny<PublishTarget>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ReturnsAsync("{\"content\":\"Generated text\"}");

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _contentGeneratorMock.Verify(
            g => g.GenerateForPlatformAsync(
                It.Is<Event>(e => e.Id == evt.Id),
                It.IsAny<PublishTarget>(),
                It.IsAny<CancellationToken>(),
                publication.UpdateContext),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P1 — Publication with null Event is skipped with a warning log
    //       (no call to IContentGenerator)
    // ------------------------------------------------------------------

    [Test]
    public async Task GenerateContentAsync_WhenPublicationHasNoEvent_SkipsWithoutCallingGenerator()
    {
        // Arrange
        var publication = CreatePublication(withEvent: null);

        _publicationRepoMock
            .Setup(r => r.GetPendingForContentGenerationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([publication]);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _contentGeneratorMock.Verify(
            g => g.GenerateForPlatformAsync(
                It.IsAny<Event>(),
                It.IsAny<PublishTarget>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Never);

        _publicationRepoMock.Verify(
            r => r.UpdateGeneratedContentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P0 — Existing publish behaviour: successful publish updates status
    //       to Published and records a success log
    // ------------------------------------------------------------------

    [Test]
    public async Task PublishReadyAsync_WhenPublicationIsContentReady_PublishesAndSetsStatusToPublished()
    {
        // Arrange
        const string externalMessageId = "tg-msg-12345";
        var publication = CreatePublication(status: PublicationStatus.ContentReady);

        _publicationRepoMock
            .Setup(r => r.GetReadyForPublishAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([publication]);

        _publisherMock
            .Setup(p => p.PublishAsync(publication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalMessageId);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _publisherMock.Verify(p => p.PublishAsync(publication, It.IsAny<CancellationToken>()), Times.Once);
        _publicationRepoMock.Verify(
            r => r.UpdateStatusAsync(publication.Id, PublicationStatus.Published, It.IsAny<CancellationToken>()),
            Times.Once);
        _publicationRepoMock.Verify(
            r => r.AddPublishLogAsync(
                It.Is<PublishLog>(l => l.Status == PublishLogStatus.Success && l.ExternalMessageId == externalMessageId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P1 — Existing retry behaviour: failed publish sets status to Failed
    //       and records a failure log
    // ------------------------------------------------------------------

    [Test]
    public async Task PublishReadyAsync_WhenPublisherThrows_SetsStatusToFailedAndLogsError()
    {
        // Arrange
        var publication = CreatePublication(status: PublicationStatus.ContentReady);

        _publicationRepoMock
            .Setup(r => r.GetReadyForPublishAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([publication]);

        _publisherMock
            .Setup(p => p.PublishAsync(publication, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Telegram API error"));

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _publicationRepoMock.Verify(
            r => r.UpdateStatusAsync(publication.Id, PublicationStatus.Failed, It.IsAny<CancellationToken>()),
            Times.Once);
        _publicationRepoMock.Verify(
            r => r.AddPublishLogAsync(
                It.Is<PublishLog>(l => l.Status == PublishLogStatus.Failed),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private PublicationWorker CreateWorker() =>
        new(_scopeFactoryMock.Object,
            NullLogger<PublicationWorker>.Instance,
            _options);

    private static async Task RunOneIterationAsync(PublicationWorker sut)
    {
        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await Task.Delay(300);
        await sut.StopAsync(CancellationToken.None);
    }

    private void SetupDefaultRepositoryBehaviours()
    {
        _publicationRepoMock
            .Setup(r => r.GetPendingForContentGenerationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _publicationRepoMock
            .Setup(r => r.GetReadyForPublishAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private void WireUpScopeFactory()
    {
        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        serviceProviderMock.Setup(sp => sp.GetService(typeof(IPublicationRepository)))
            .Returns(_publicationRepoMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IContentGenerator)))
            .Returns(_contentGeneratorMock.Object);

        // GetServices<IPublisher>() is resolved via IEnumerable<IPublisher>
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IEnumerable<IPublisher>)))
            .Returns(new[] { _publisherMock.Object });

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
    }

    private static Event CreateEvent() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Event",
        Summary = "Event summary.",
        Status = EventStatus.Approved,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        Articles = [CreateArticleForEvent()]
    };

    private static Article CreateArticleForEvent() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Initiator Article",
        Role = ArticleRole.Initiator
    };

    private static Publication CreatePublication(
        Event? withEvent = null,
        PublicationStatus status = PublicationStatus.Pending)
    {
        var target = new PublishTarget
        {
            Id = Guid.NewGuid(),
            Name = "Test Channel",
            Platform = Platform.Telegram,
            Identifier = "@test",
            IsActive = true
        };

        return new Publication
        {
            Id = Guid.NewGuid(),
            Article = new Article
            {
                Id = Guid.NewGuid(),
                Title = "Article"
            },
            PublishTarget = target,
            PublishTargetId = target.Id,
            Status = status,
            GeneratedContent = "Ready content.",
            CreatedAt = DateTimeOffset.UtcNow,
            Event = withEvent,
            EventId = withEvent?.Id
        };
    }
}
