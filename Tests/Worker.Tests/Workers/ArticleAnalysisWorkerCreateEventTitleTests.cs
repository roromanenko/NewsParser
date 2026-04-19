using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
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
/// Tests focused on title generation during new event creation inside
/// ArticleAnalysisWorker.CreateNewEventAsync.
///
/// FindSimilarEventsAsync is configured to return an empty list for all tests,
/// which forces the worker down the new-event-creation path where
/// IEventTitleGenerator.GenerateTitleAsync is called before CreateAsync.
/// </summary>
[TestFixture]
public class ArticleAnalysisWorkerCreateEventTitleTests
{
    private Mock<IServiceScopeFactory> _scopeFactoryMock = null!;
    private Mock<IArticleRepository> _articleRepoMock = null!;
    private Mock<IEventRepository> _eventRepoMock = null!;
    private Mock<IArticleAnalyzer> _analyzerMock = null!;
    private Mock<IGeminiEmbeddingService> _embeddingServiceMock = null!;
    private Mock<IEventClassifier> _classifierMock = null!;
    private Mock<IEventSummaryUpdater> _summaryUpdaterMock = null!;
    private Mock<IKeyFactsExtractor> _keyFactsExtractorMock = null!;
    private Mock<IContradictionDetector> _contradictionDetectorMock = null!;
    private Mock<IEventTitleGenerator> _titleGeneratorMock = null!;
    private Mock<IEventImportanceScorer> _scorerMock = null!;

    private IOptions<ArticleProcessingOptions> _processingOptions = null!;
    private IOptions<AiOptions> _aiOptions = null!;

    [SetUp]
    public void SetUp()
    {
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _articleRepoMock = new Mock<IArticleRepository>();
        _eventRepoMock = new Mock<IEventRepository>();
        _analyzerMock = new Mock<IArticleAnalyzer>();
        _embeddingServiceMock = new Mock<IGeminiEmbeddingService>();
        _classifierMock = new Mock<IEventClassifier>();
        _summaryUpdaterMock = new Mock<IEventSummaryUpdater>();
        _keyFactsExtractorMock = new Mock<IKeyFactsExtractor>();
        _contradictionDetectorMock = new Mock<IContradictionDetector>();
        _titleGeneratorMock = new Mock<IEventTitleGenerator>();
        _scorerMock = new Mock<IEventImportanceScorer>();

        _processingOptions = Options.Create(new ArticleProcessingOptions
        {
            AnalysisIntervalSeconds = 9999,
            BatchSize = 10,
            MaxRetryCount = 5,
            DeduplicationThreshold = 0.95,
            DeduplicationWindowHours = 72,
            AutoSameEventThreshold = 0.90,
            AutoNewEventThreshold = 0.70,
            SimilarityWindowHours = 24,
            AnalyzeAutoMatchUpdates = true,
            MaxUpdatesPerDay = 10,
            MinUpdateIntervalMinutes = 30,
        });

        _aiOptions = Options.Create(new AiOptions
        {
            Gemini = new GeminiOptions { AnalyzerModel = "gemini-2.0-flash" },
            Anthropic = new AnthropicOptions()
        });

        SetupDefaultRepositoryBehaviours();
        WireUpScopeFactory();
    }

    // ------------------------------------------------------------------
    // P0 — When TitleGenerator returns a non-empty Ukrainian string,
    //       CreateAsync is called with that string as the event Title
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateNewEventAsync_WhenTitleGeneratorReturnsNonEmptyString_CreatesEventWithGeneratedTitle()
    {
        // Arrange
        const string generatedTitle = "Масштабна повінь на заході України: тисячі евакуйованих";
        var article = CreatePendingArticle();

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _titleGeneratorMock
            .Setup(g => g.GenerateTitleAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(generatedTitle);

        Event? capturedEvent = null;
        _eventRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .Callback<Event, CancellationToken>((evt, _) => capturedEvent = evt)
            .ReturnsAsync((Event e, CancellationToken _) => e);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Title.Should().Be(generatedTitle);
    }

    // ------------------------------------------------------------------
    // P1 — When TitleGenerator returns an empty string,
    //       CreateAsync is called with the article Title as fallback
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateNewEventAsync_WhenTitleGeneratorReturnsEmptyString_CreatesEventWithArticleTitleAsFallback()
    {
        // Arrange
        var article = CreatePendingArticle();

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _titleGeneratorMock
            .Setup(g => g.GenerateTitleAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        Event? capturedEvent = null;
        _eventRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .Callback<Event, CancellationToken>((evt, _) => capturedEvent = evt)
            .ReturnsAsync((Event e, CancellationToken _) => e);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Title.Should().Be(article.Title);
    }

    // ------------------------------------------------------------------
    // P1 — When TitleGenerator throws a non-cancellation exception,
    //       the event is still created with the article Title (exception is caught and logged)
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateNewEventAsync_WhenTitleGeneratorThrowsNonCancellationException_CreatesEventWithArticleTitleAsFallback()
    {
        // Arrange
        var article = CreatePendingArticle();

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _titleGeneratorMock
            .Setup(g => g.GenerateTitleAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("AI rate limit exceeded"));

        Event? capturedEvent = null;
        _eventRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .Callback<Event, CancellationToken>((evt, _) => capturedEvent = evt)
            .ReturnsAsync((Event e, CancellationToken _) => e);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert — CreateAsync must still be called and the event must bear the article title
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Title.Should().Be(article.Title);

        _eventRepoMock.Verify(
            r => r.CreateAsync(It.IsAny<Event>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private ArticleAnalysisWorker CreateWorker() =>
        new(_scopeFactoryMock.Object,
            NullLogger<ArticleAnalysisWorker>.Instance,
            _processingOptions,
            _aiOptions);

    private static async Task RunOneIterationAsync(ArticleAnalysisWorker sut)
    {
        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await Task.Delay(300);
        await sut.StopAsync(CancellationToken.None);
    }

    private void SetupDefaultRepositoryBehaviours()
    {
        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _analyzerMock
            .Setup(a => a.AnalyzeAsync(It.IsAny<Article>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArticleAnalysisResult
            {
                Category = "Politics",
                Tags = ["ukraine", "flood"],
                Sentiment = "Neutral",
                Language = "uk",
                Summary = "A major flood has struck western Ukraine."
            });

        _embeddingServiceMock
            .Setup(s => s.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[384]);

        _keyFactsExtractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Article>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // No similar events → forces new-event creation path
        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _eventRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Event e, CancellationToken _) => e);

        _eventRepoMock
            .Setup(r => r.GetImportanceStatsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventImportanceStats(0, 0, 0, null));

        _eventRepoMock
            .Setup(r => r.UpdateImportanceAsync(
                It.IsAny<Guid>(),
                It.IsAny<ImportanceTier>(),
                It.IsAny<double>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _contradictionDetectorMock
            .Setup(d => d.DetectAsync(It.IsAny<Article>(), It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _titleGeneratorMock
            .Setup(g => g.GenerateTitleAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Тестовий заголовок події");

        _summaryUpdaterMock
            .Setup(u => u.UpdateSummaryAsync(It.IsAny<Event>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Event evt, List<string> _, CancellationToken _) =>
                new EventSummaryUpdateResult(evt.Summary ?? string.Empty, "medium"));

        _scorerMock
            .Setup(s => s.Calculate(It.IsAny<ImportanceInputs>()))
            .Returns(new ImportanceScoreResult(0.0, 0.0, ImportanceTier.Low));
    }

    private void WireUpScopeFactory()
    {
        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        serviceProviderMock.Setup(sp => sp.GetService(typeof(IArticleRepository)))
            .Returns(_articleRepoMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IEventRepository)))
            .Returns(_eventRepoMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IArticleAnalyzer)))
            .Returns(_analyzerMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IGeminiEmbeddingService)))
            .Returns(_embeddingServiceMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IEventClassifier)))
            .Returns(_classifierMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IEventSummaryUpdater)))
            .Returns(_summaryUpdaterMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IKeyFactsExtractor)))
            .Returns(_keyFactsExtractorMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IContradictionDetector)))
            .Returns(_contradictionDetectorMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IEventTitleGenerator)))
            .Returns(_titleGeneratorMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IEventImportanceScorer)))
            .Returns(_scorerMock.Object);

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
    }

    private static Article CreatePendingArticle() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Повінь на заході України",
        Summary = "A major flood has struck western Ukraine displacing thousands.",
        Status = ArticleStatus.Pending,
        ProcessedAt = DateTimeOffset.UtcNow,
        KeyFacts = [],
    };
}
