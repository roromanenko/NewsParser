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
/// Tests focused on the grey-zone branch inside
/// ArticleAnalysisWorker.ClassifyIntoEventAsync when the top similarity
/// score falls between AutoNewEventThreshold and AutoSameEventThreshold.
///
/// In the grey-zone path the worker:
///   1. Calls GetWithContextAsync for each candidate event to load an enriched version.
///   2. Passes the enriched candidates to IEventClassifier.ClassifyAsync.
///   3. Falls back to the lightweight candidate events if all GetWithContextAsync calls return null.
/// </summary>
[TestFixture]
public class ArticleAnalysisWorkerGreyZoneEnrichedTests
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
    private Mock<IProjectRepository> _projectRepoMock = null!;

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
        _projectRepoMock = new Mock<IProjectRepository>();

        // AutoSameEventThreshold=0.90, AutoNewEventThreshold=0.70
        // A grey-zone similarity is in (0.70, 0.90) — tests use 0.80
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
    // P0 — When GetWithContextAsync returns an enriched candidate, ClassifyAsync
    //       is called with that enriched candidate (which has non-empty Articles)
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenGetWithContextReturnsEnrichedCandidate_ClassifyAsyncReceivesCandidateWithNonEmptyArticles()
    {
        // Arrange
        var article = CreatePendingArticle();
        var lightweightCandidate = CreateActiveEvent();
        var enrichedCandidate = CreateEnrichedEvent(lightweightCandidate.Id);

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        // Similarity 0.80 is in the grey zone (> 0.70 but < 0.90)
        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(It.IsAny<Guid>(), It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(lightweightCandidate, 0.80)]);

        _eventRepoMock
            .Setup(r => r.GetWithContextAsync(lightweightCandidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enrichedCandidate);

        List<Event>? capturedCandidates = null;
        _classifierMock
            .Setup(c => c.ClassifyAsync(It.IsAny<Article>(), It.IsAny<List<Event>>(), It.IsAny<CancellationToken>()))
            .Callback<Article, List<Event>, CancellationToken>((_, candidates, _) => capturedCandidates = candidates)
            .ReturnsAsync(new EventClassificationResult { IsNewEvent = true });

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert — ClassifyAsync must receive the enriched candidate with non-empty Articles
        capturedCandidates.Should().NotBeNull();
        capturedCandidates.Should().HaveCount(1);
        capturedCandidates![0].Articles.Should().NotBeEmpty(
            "the grey-zone path must pass the enriched candidate (with Articles populated) to the classifier");
    }

    // ------------------------------------------------------------------
    // P1 — When GetWithContextAsync returns null for all candidates, the classifier
    //       is still called with the lightweight fallback candidates
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenGetWithContextReturnsNullForAllCandidates_ClassifierIsStillCalledWithLightweightFallback()
    {
        // Arrange
        var article = CreatePendingArticle();
        var lightweightCandidate = CreateActiveEvent();

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(It.IsAny<Guid>(), It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(lightweightCandidate, 0.80)]);

        // GetWithContextAsync returns null → enrichedCandidates will be empty → fallback to lightweight
        _eventRepoMock
            .Setup(r => r.GetWithContextAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Event?)null);

        List<Event>? capturedCandidates = null;
        _classifierMock
            .Setup(c => c.ClassifyAsync(It.IsAny<Article>(), It.IsAny<List<Event>>(), It.IsAny<CancellationToken>()))
            .Callback<Article, List<Event>, CancellationToken>((_, candidates, _) => capturedCandidates = candidates)
            .ReturnsAsync(new EventClassificationResult { IsNewEvent = true });

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert — ClassifyAsync must still be called, but receives the lightweight fallback candidate
        _classifierMock.Verify(
            c => c.ClassifyAsync(It.IsAny<Article>(), It.IsAny<List<Event>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        capturedCandidates.Should().NotBeNull();
        capturedCandidates.Should().HaveCount(1);
        capturedCandidates![0].Id.Should().Be(lightweightCandidate.Id,
            "when all GetWithContextAsync calls return null the fallback must be the original lightweight event");
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

        _projectRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);

        _analyzerMock
            .Setup(a => a.AnalyzeAsync(It.IsAny<Article>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArticleAnalysisResult
            {
                Category = "Technology",
                Tags = ["ai", "news"],
                Sentiment = "Neutral",
                Language = "en",
                Summary = "Analyzed summary."
            });

        _embeddingServiceMock
            .Setup(s => s.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[384]);


        _keyFactsExtractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Article>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(It.IsAny<Guid>(), It.IsAny<float[]>(),
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
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IProjectRepository)))
            .Returns(_projectRepoMock.Object);
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
        Title = "Grey Zone Test Article",
        Status = ArticleStatus.Pending,
        ProcessedAt = DateTimeOffset.UtcNow,
        KeyFacts = [],
    };

    private static Event CreateActiveEvent() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Grey Zone Candidate Event",
        Summary = "A candidate event in the grey zone similarity range.",
        Status = EventStatus.Active,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        Embedding = new float[384],
        ArticleCount = 1,
        EventUpdates = [],
        Articles = [],
    };

    private static Event CreateEnrichedEvent(Guid eventId) => new()
    {
        Id = eventId,
        Title = "Grey Zone Candidate Event",
        Summary = "A candidate event in the grey zone similarity range.",
        Status = EventStatus.Active,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        Embedding = new float[384],
        ArticleCount = 1,
        EventUpdates =
        [
            new EventUpdate
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                ArticleId = Guid.NewGuid(),
                FactSummary = "Initial fact for the event.",
                CreatedAt = DateTimeOffset.UtcNow,
            }
        ],
        Articles =
        [
            new Article
            {
                Id = Guid.NewGuid(),
                Title = "Original article for this event",
                KeyFacts = ["initial key fact"],
                Status = ArticleStatus.AnalysisDone,
                ProcessedAt = DateTimeOffset.UtcNow,
            }
        ],
    };
}

