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
/// Tests focused on the key-facts extraction phase (Phase A2) inside
/// ArticleAnalysisWorker.ProcessArticleAsync → ExtractAndPersistKeyFactsAsync.
///
/// The worker loops on Task.Delay; to keep tests fast the analysis interval
/// is set to 9999 seconds so the loop body runs exactly once before StopAsync
/// cancels the delay.
/// </summary>
[TestFixture]
public class ArticleAnalysisWorkerKeyFactsTests
{
    private Mock<IServiceScopeFactory> _scopeFactoryMock = null!;
    private Mock<IArticleRepository> _articleRepoMock = null!;
    private Mock<IEventRepository> _eventRepoMock = null!;
    private Mock<IArticleAnalyzer> _analyzerMock = null!;
    private Mock<IGeminiEmbeddingService> _embeddingServiceMock = null!;
    private Mock<IEventClassifier> _classifierMock = null!;
    private Mock<IEventSummaryUpdater> _summaryUpdaterMock = null!;
    private Mock<IKeyFactsExtractor> _keyFactsExtractorMock = null!;

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
    // P0 — Key facts are extracted and persisted after the analysis result
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenKeyFactsExtracted_PersistsKeyFactsViaRepository()
    {
        // Arrange
        var article = CreatePendingArticle();
        var extractedFacts = new List<string> { "Fact one.", "Fact two.", "Fact three." };

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _keyFactsExtractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Article>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractedFacts);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _keyFactsExtractorMock.Verify(
            e => e.ExtractAsync(It.Is<Article>(a => a.Id == article.Id), It.IsAny<CancellationToken>()),
            Times.Once);
        _articleRepoMock.Verify(
            r => r.UpdateKeyFactsAsync(article.Id, extractedFacts, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P1 — IKeyFactsExtractor failure logs a warning but article continues
    //       to the embedding phase
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenKeyFactsExtractorThrows_ContinuesToEmbeddingPhase()
    {
        // Arrange
        var article = CreatePendingArticle();

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _keyFactsExtractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Article>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("AI service unavailable"));

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert — embedding phase proceeds; UpdateKeyFactsAsync is not called on failure
        _embeddingServiceMock.Verify(
            s => s.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _articleRepoMock.Verify(
            r => r.UpdateKeyFactsAsync(It.IsAny<Guid>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P0 — UpdateKeyFactsAsync is called with the extracted list
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenExtractorReturnsEmptyList_CallsUpdateKeyFactsWithEmptyList()
    {
        // Arrange
        var article = CreatePendingArticle();

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _keyFactsExtractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Article>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _articleRepoMock.Verify(
            r => r.UpdateKeyFactsAsync(
                article.Id,
                It.Is<List<string>>(l => l.Count == 0),
                It.IsAny<CancellationToken>()),
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
        // Default: no pending articles
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

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
    }

    private static Article CreatePendingArticle() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Article for Key Facts",
        Content = "Article content long enough for analysis.",
        Status = ArticleStatus.Pending,
        ProcessedAt = DateTimeOffset.UtcNow
    };
}
