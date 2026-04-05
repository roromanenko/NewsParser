using Core.DomainModels;
using Core.Interfaces.AI;
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
/// Tests focused on the duplicate-detection path inside
/// ArticleAnalysisWorker.ProcessArticleAsync.
///
/// When HasSimilarAsync returns true the worker must call
/// RejectAsync(id, "duplicate_by_vector", ct) and stop — no embedding
/// is persisted and the article is not classified into an event.
/// </summary>
[TestFixture]
public class ArticleAnalysisWorkerDeduplicationTests
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

        _processingOptions = Options.Create(new ArticleProcessingOptions
        {
            AnalysisIntervalSeconds = 9999,
            BatchSize = 10,
            MaxRetryCount = 5,
            DeduplicationThreshold = 0.95,
            DeduplicationWindowHours = 72,
            AutoSameEventThreshold = 0.90,
            AutoNewEventThreshold = 0.70,
            SimilarityWindowHours = 24
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
    // P0 — When HasSimilarAsync returns true, RejectAsync is called
    //       with the article id and the "duplicate_by_vector" reason
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenArticleIsDuplicate_CallsRejectAsyncWithDuplicateReason()
    {
        // Arrange
        var article = CreatePendingArticle();

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _articleRepoMock
            .Setup(r => r.HasSimilarAsync(
                article.Id,
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _articleRepoMock.Verify(
            r => r.RejectAsync(article.Id, "duplicate_by_vector", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P0 — When the article is a duplicate, UpdateEmbeddingAsync is NOT
    //       called — the embedding must not be stored for rejected duplicates
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenArticleIsDuplicate_DoesNotPersistEmbedding()
    {
        // Arrange
        var article = CreatePendingArticle();

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _articleRepoMock
            .Setup(r => r.HasSimilarAsync(
                article.Id,
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _articleRepoMock.Verify(
            r => r.UpdateEmbeddingAsync(It.IsAny<Guid>(), It.IsAny<float[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P0 — When the article is a duplicate, it is NOT classified into an
    //       event — AssignArticleToEventAsync must not be called
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenArticleIsDuplicate_DoesNotClassifyIntoEvent()
    {
        // Arrange
        var article = CreatePendingArticle();

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _articleRepoMock
            .Setup(r => r.HasSimilarAsync(
                article.Id,
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _eventRepoMock.Verify(
            r => r.AssignArticleToEventAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ArticleRole>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P1 — When the article is NOT a duplicate, RejectAsync is NOT called
    //       (ensures the duplicate path is taken only on a genuine match)
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenArticleIsNotDuplicate_DoesNotCallRejectAsync()
    {
        // Arrange
        var article = CreatePendingArticle();

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        // HasSimilarAsync already returns false via SetupDefaultRepositoryBehaviours

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _articleRepoMock.Verify(
            r => r.RejectAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
            .ReturnsAsync(new Core.DomainModels.AI.ArticleAnalysisResult
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

        _articleRepoMock
            .Setup(r => r.HasSimilarAsync(
                It.IsAny<Guid>(),
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _keyFactsExtractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Article>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _eventRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Event e, CancellationToken _) => e);

        _contradictionDetectorMock
            .Setup(d => d.DetectAsync(It.IsAny<Article>(), It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
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

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
    }

    private static Article CreatePendingArticle() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Duplicate Detection Test Article",
        Status = ArticleStatus.Pending,
        ProcessedAt = DateTimeOffset.UtcNow
    };
}
