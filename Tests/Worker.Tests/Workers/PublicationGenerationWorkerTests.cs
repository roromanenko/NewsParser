using Core.DomainModels;
using Core.Interfaces.AI;
using Core.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Worker.Configuration;
using Worker.Workers;

namespace Worker.Tests.Workers;

/// <summary>
/// Unit tests for <see cref="PublicationGenerationWorker"/> content-generation dispatch.
/// All dependencies are mocked; one short iteration is driven through
/// <c>StartAsync</c>/<c>StopAsync</c> with a long interval so the loop executes once.
///
/// Focus: verify that <see cref="IContentGenerator.GenerateForPlatformAsync"/>
/// receives the correct <c>editorFeedback</c> / <c>updateContext</c> values
/// depending on which fields are set on the publication.
/// </summary>
[TestFixture]
public class PublicationGenerationWorkerTests
{
    private Mock<IPublicationRepository> _publicationRepoMock = null!;
    private Mock<IContentGenerator> _contentGeneratorMock = null!;
    private Mock<IServiceScopeFactory> _scopeFactoryMock = null!;

    private IOptions<PublicationGenerationWorkerOptions> _workerOptions = null!;

    [SetUp]
    public void SetUp()
    {
        _publicationRepoMock = new Mock<IPublicationRepository>();
        _contentGeneratorMock = new Mock<IContentGenerator>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();

        // Very long interval so the worker loop only runs one iteration before cancellation.
        _workerOptions = Options.Create(new PublicationGenerationWorkerOptions
        {
            IntervalSeconds = 9999,
            BatchSize = 10,
        });

        _contentGeneratorMock
            .Setup(g => g.GenerateForPlatformAsync(
                It.IsAny<Event>(),
                It.IsAny<PublishTarget>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync("Generated content");
    }

    // ------------------------------------------------------------------
    // P0 — EditorFeedback is set → generator called with editorFeedback=value
    //      and updateContext=null
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessBatchAsync_WhenEditorFeedbackIsSet_CallsGeneratorWithEditorFeedbackAndNullUpdateContext()
    {
        // Arrange
        const string feedback = "Make it shorter and drop the political angle.";
        var publication = CreatePublication(editorFeedback: feedback, updateContext: null);

        _publicationRepoMock
            .Setup(r => r.GetPendingForGenerationAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([publication]);

        WireUpScopeFactory();

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _contentGeneratorMock.Verify(
            g => g.GenerateForPlatformAsync(
                publication.Event!,
                publication.PublishTarget,
                It.IsAny<CancellationToken>(),
                null,
                feedback),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P1 — Only UpdateContext is set → generator called with updateContext=value
    //      and editorFeedback=null (regression guard)
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessBatchAsync_WhenOnlyUpdateContextIsSet_CallsGeneratorWithUpdateContextAndNullEditorFeedback()
    {
        // Arrange
        const string updateContext = "New development: official statement issued at 14:00.";
        var publication = CreatePublication(editorFeedback: null, updateContext: updateContext);

        _publicationRepoMock
            .Setup(r => r.GetPendingForGenerationAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([publication]);

        WireUpScopeFactory();

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _contentGeneratorMock.Verify(
            g => g.GenerateForPlatformAsync(
                publication.Event!,
                publication.PublishTarget,
                It.IsAny<CancellationToken>(),
                updateContext,
                null),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P2 — Both EditorFeedback and UpdateContext are null → both optional
    //      arguments are null (default initial-generation path)
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessBatchAsync_WhenBothEditorFeedbackAndUpdateContextAreNull_CallsGeneratorWithBothNull()
    {
        // Arrange
        var publication = CreatePublication(editorFeedback: null, updateContext: null);

        _publicationRepoMock
            .Setup(r => r.GetPendingForGenerationAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([publication]);

        WireUpScopeFactory();

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _contentGeneratorMock.Verify(
            g => g.GenerateForPlatformAsync(
                publication.Event!,
                publication.PublishTarget,
                It.IsAny<CancellationToken>(),
                null,
                null),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private PublicationGenerationWorker CreateWorker() =>
        new(
            _scopeFactoryMock.Object,
            NullLogger<PublicationGenerationWorker>.Instance,
            _workerOptions);

    /// <summary>
    /// Starts the worker, lets it process one full iteration, then stops it.
    /// The Task.Delay(9999 s) in the loop is cancelled by StopAsync.
    /// </summary>
    private static async Task RunOneIterationAsync(PublicationGenerationWorker sut)
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
            .Setup(sp => sp.GetService(typeof(IContentGenerator)))
            .Returns(_contentGeneratorMock.Object);

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
    }

    private static Publication CreatePublication(string? editorFeedback, string? updateContext)
    {
        var article = new Article
        {
            Id = Guid.NewGuid(),
            Title = "Test Article",
            Role = ArticleRole.Initiator
        };

        var evt = new Event
        {
            Id = Guid.NewGuid(),
            Title = "Test Event",
            Summary = "Test Summary",
            Status = EventStatus.Active,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Articles = [article]
        };

        return new Publication
        {
            Id = Guid.NewGuid(),
            Article = article,
            PublishTarget = new PublishTarget
            {
                Id = Guid.NewGuid(),
                Name = "Test Channel",
                Platform = Platform.Telegram,
                Identifier = "@test",
                IsActive = true
            },
            Status = PublicationStatus.Created,
            CreatedAt = DateTimeOffset.UtcNow,
            EventId = evt.Id,
            Event = evt,
            EditorFeedback = editorFeedback,
            UpdateContext = updateContext,
        };
    }
}
