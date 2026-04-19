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
/// Tests focused on the contradiction-detection branch inside
/// ArticleAnalysisWorker.ClassifyIntoEventAsync when the article
/// auto-matches an existing event (similarity >= AutoSameEventThreshold).
///
/// When IContradictionDetector.DetectAsync returns contradictions the worker
/// must assign ArticleRole.Contradiction and persist them via AddContradictionAsync.
/// When DetectAsync returns an empty list the worker assigns ArticleRole.Update
/// and must NOT call AddContradictionAsync.
///
/// Additional tests cover:
/// - GetWithContextAsync enrichment: the worker loads an enriched event and passes
///   it to DetectAsync; if null is returned the lightweight event is used as fallback.
/// - AnalyzeAutoMatchUpdates flag: when true and a new key fact exists, ClassifyAsync
///   is called and a significant update triggers AddEventUpdateAsync.
/// - Key-fact pre-filter: ClassifyAsync is NOT called when all key facts are already
///   present in the event summary.
/// - Title generation: TitleGenerator.GenerateTitleAsync is called when updating an
///   event embedding; an empty result falls back to the existing event title.
/// </summary>
[TestFixture]
public class ArticleAnalysisWorkerAutoMatchContradictionTests
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
    // P0 — When DetectAsync returns one contradiction, role is Contradiction
    //       and AddContradictionAsync is called once
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenContradictionDetected_AssignsContradictionRoleAndPersistsContradiction()
    {
        // Arrange
        var article = CreatePendingArticle();
        var autoMatchedEvent = CreateActiveEvent();
        var contradictionInput = new ContradictionInput
        {
            ArticleIds = [article.Id],
            Description = "Article claims 10 casualties, event summary says 3."
        };

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(autoMatchedEvent, 0.95)]); // above AutoSameEventThreshold of 0.90

        _contradictionDetectorMock
            .Setup(d => d.DetectAsync(It.IsAny<Article>(), It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([contradictionInput]);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _eventRepoMock.Verify(
            r => r.AssignArticleToEventAsync(
                article.Id,
                autoMatchedEvent.Id,
                ArticleRole.Contradiction,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _eventRepoMock.Verify(
            r => r.AddContradictionAsync(
                It.IsAny<Contradiction>(),
                It.IsAny<List<Guid>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P0 — When DetectAsync returns empty list, role is Update and
    //       AddContradictionAsync is NOT called
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenNoContradictionDetected_AssignsUpdateRoleAndDoesNotPersistContradiction()
    {
        // Arrange
        var article = CreatePendingArticle();
        var autoMatchedEvent = CreateActiveEvent();

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(autoMatchedEvent, 0.95)]);

        _contradictionDetectorMock
            .Setup(d => d.DetectAsync(It.IsAny<Article>(), It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _eventRepoMock.Verify(
            r => r.AssignArticleToEventAsync(
                article.Id,
                autoMatchedEvent.Id,
                ArticleRole.Update,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _eventRepoMock.Verify(
            r => r.AddContradictionAsync(
                It.IsAny<Contradiction>(),
                It.IsAny<List<Guid>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P1 — DetectAsync is called with the auto-matched event (not any other)
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenAutoMatchOccurs_CallsDetectAsyncWithTheMatchedEvent()
    {
        // Arrange
        var article = CreatePendingArticle();
        var autoMatchedEvent = CreateActiveEvent();

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(autoMatchedEvent, 0.95)]);

        _contradictionDetectorMock
            .Setup(d => d.DetectAsync(It.IsAny<Article>(), It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert — DetectAsync must be called with the exact auto-matched event
        _contradictionDetectorMock.Verify(
            d => d.DetectAsync(
                It.Is<Article>(a => a.Id == article.Id),
                It.Is<Event>(e => e.Id == autoMatchedEvent.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P0 — When GetWithContextAsync returns an enriched event,
    //       DetectAsync is called with the enriched event (has non-empty Articles)
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenGetWithContextReturnsEnrichedEvent_DetectAsyncReceivesEventWithNonEmptyArticles()
    {
        // Arrange
        var article = CreatePendingArticle();
        var lightweightEvent = CreateActiveEvent();
        var enrichedEvent = CreateEnrichedEvent(lightweightEvent.Id);

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(lightweightEvent, 0.95)]);

        _eventRepoMock
            .Setup(r => r.GetWithContextAsync(lightweightEvent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enrichedEvent);

        Event? capturedEvent = null;
        _contradictionDetectorMock
            .Setup(d => d.DetectAsync(It.IsAny<Article>(), It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .Callback<Article, Event, CancellationToken>((_, evt, _) => capturedEvent = evt)
            .ReturnsAsync([]);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert — DetectAsync must receive the enriched event that has non-empty Articles
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Articles.Should().NotBeEmpty(
            "the enriched event loaded by GetWithContextAsync must be passed to DetectAsync");
    }

    // ------------------------------------------------------------------
    // P0 — When GetWithContextAsync returns null, ClassifyAsync is NOT called
    //       on the auto-match path (AnalyzeAutoMatchUpdateAsync is skipped entirely)
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenGetWithContextReturnsNull_ClassifyAsyncIsNotCalledOnAutoMatchPath()
    {
        // Arrange
        var article = CreatePendingArticle();
        var lightweightEvent = CreateActiveEvent();

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(lightweightEvent, 0.95)]);

        _eventRepoMock
            .Setup(r => r.GetWithContextAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Event?)null);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert — enriched event is null, so AnalyzeAutoMatchUpdateAsync is skipped
        _classifierMock.Verify(
            c => c.ClassifyAsync(It.IsAny<Article>(), It.IsAny<List<Event>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P0 — When AnalyzeAutoMatchUpdates=false, ClassifyAsync is NOT called
    //       on the auto-match path even when a new key fact is present
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenAnalyzeAutoMatchUpdatesFalse_ClassifyAsyncIsNotCalledOnAutoMatchPath()
    {
        // Arrange — rebuild options with AnalyzeAutoMatchUpdates disabled
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
            AnalyzeAutoMatchUpdates = false,
            MaxUpdatesPerDay = 10,
            MinUpdateIntervalMinutes = 30,
        });

        var article = CreatePendingArticle(keyFacts: ["completely new fact not in summary"]);
        var lightweightEvent = CreateActiveEvent();
        var enrichedEvent = CreateEnrichedEvent(lightweightEvent.Id);

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(lightweightEvent, 0.95)]);

        _eventRepoMock
            .Setup(r => r.GetWithContextAsync(lightweightEvent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enrichedEvent);

        // Rebuild the scope with the updated options
        WireUpScopeFactory();

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _classifierMock.Verify(
            c => c.ClassifyAsync(It.IsAny<Article>(), It.IsAny<List<Event>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P0 — When classifier returns IsSignificantUpdate=true and NewFacts has items,
    //       AddEventUpdateAsync is called once
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenClassifierReturnsSignificantUpdateWithNewFacts_CallsAddEventUpdateAsyncOnce()
    {
        // Arrange
        var article = CreatePendingArticle(keyFacts: ["new fact absent from event summary"]);
        var lightweightEvent = CreateActiveEvent();
        var enrichedEvent = CreateEnrichedEvent(lightweightEvent.Id);

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(lightweightEvent, 0.95)]);

        _eventRepoMock
            .Setup(r => r.GetWithContextAsync(lightweightEvent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enrichedEvent);

        _keyFactsExtractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Article>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["new fact absent from event summary"]);

        _classifierMock
            .Setup(c => c.ClassifyAsync(It.IsAny<Article>(), It.IsAny<List<Event>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventClassificationResult
            {
                IsNewEvent = false,
                MatchedEventId = enrichedEvent.Id,
                IsSignificantUpdate = true,
                NewFacts = ["New casualty count confirmed."],
            });

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _eventRepoMock.Verify(
            r => r.AddEventUpdateAsync(
                It.Is<EventUpdate>(u => u.EventId == enrichedEvent.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P1 — When classifier returns IsSignificantUpdate=false,
    //       AddEventUpdateAsync is NOT called
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenClassifierReturnsIsSignificantUpdateFalse_AddEventUpdateAsyncIsNotCalled()
    {
        // Arrange
        var article = CreatePendingArticle(keyFacts: ["new fact absent from event summary"]);
        var lightweightEvent = CreateActiveEvent();
        var enrichedEvent = CreateEnrichedEvent(lightweightEvent.Id);

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(lightweightEvent, 0.95)]);

        _eventRepoMock
            .Setup(r => r.GetWithContextAsync(lightweightEvent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enrichedEvent);

        _keyFactsExtractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Article>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["new fact absent from event summary"]);

        _classifierMock
            .Setup(c => c.ClassifyAsync(It.IsAny<Article>(), It.IsAny<List<Event>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventClassificationResult
            {
                IsNewEvent = false,
                MatchedEventId = enrichedEvent.Id,
                IsSignificantUpdate = false,
                NewFacts = [],
            });

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert
        _eventRepoMock.Verify(
            r => r.AddEventUpdateAsync(It.IsAny<EventUpdate>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P1 — When all article key facts are already present in the event summary
    //       (case-insensitive substring match), ClassifyAsync is NOT called
    //       even when AnalyzeAutoMatchUpdates=true
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenAllKeyFactsAlreadyInSummary_ClassifyAsyncIsNotCalled()
    {
        // Arrange
        const string existingFact = "7 people injured";

        var article = CreatePendingArticle(keyFacts: [existingFact]);
        var lightweightEvent = CreateActiveEvent();

        // Enriched event summary already contains the fact (different casing)
        var enrichedEvent = CreateEnrichedEventWithSummary(
            lightweightEvent.Id,
            $"Known: {existingFact.ToUpperInvariant()}.");

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(lightweightEvent, 0.95)]);

        _eventRepoMock
            .Setup(r => r.GetWithContextAsync(lightweightEvent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enrichedEvent);

        _keyFactsExtractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Article>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingFact]);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert — all key facts already present → pre-filter skips ClassifyAsync
        _classifierMock.Verify(
            c => c.ClassifyAsync(It.IsAny<Article>(), It.IsAny<List<Event>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P1 — When at least one article key fact is absent from the event summary,
    //       ClassifyAsync IS called
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessArticleAsync_WhenAtLeastOneKeyFactAbsentFromSummary_ClassifyAsyncIsCalled()
    {
        // Arrange
        var article = CreatePendingArticle(keyFacts: ["fact already in summary", "brand new fact"]);
        var lightweightEvent = CreateActiveEvent();
        var enrichedEvent = CreateEnrichedEventWithSummary(
            lightweightEvent.Id,
            "Known: fact already in summary.");

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(lightweightEvent, 0.95)]);

        _eventRepoMock
            .Setup(r => r.GetWithContextAsync(lightweightEvent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enrichedEvent);

        _keyFactsExtractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<Article>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["fact already in summary", "brand new fact"]);

        _classifierMock
            .Setup(c => c.ClassifyAsync(It.IsAny<Article>(), It.IsAny<List<Event>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventClassificationResult
            {
                IsSignificantUpdate = false,
                NewFacts = [],
            });

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert — "brand new fact" is not in the summary → ClassifyAsync is called
        _classifierMock.Verify(
            c => c.ClassifyAsync(
                It.Is<Article>(a => a.Id == article.Id),
                It.IsAny<List<Event>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P0 — When UpdateEventEmbeddingAsync runs on the auto-match path,
    //       TitleGenerator.GenerateTitleAsync is called once
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateEventEmbeddingAsync_WhenAutoMatchPathExecutes_CallsTitleGeneratorOnce()
    {
        // Arrange
        var article = CreatePendingArticle();
        var lightweightEvent = CreateActiveEvent();
        var enrichedEvent = CreateEnrichedEvent(lightweightEvent.Id);

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(lightweightEvent, 0.95)]);

        _eventRepoMock
            .Setup(r => r.GetWithContextAsync(lightweightEvent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enrichedEvent);

        _contradictionDetectorMock
            .Setup(d => d.DetectAsync(It.IsAny<Article>(), It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert — title generator is called exactly once with non-null arguments
        _titleGeneratorMock.Verify(
            g => g.GenerateTitleAsync(
                It.IsAny<string>(),
                It.IsNotNull<List<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P1 — When TitleGenerator returns empty string, the existing event
    //       title is used as the fallback passed to UpdateSummaryTitleAndEmbeddingAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateEventEmbeddingAsync_WhenTitleGeneratorReturnsEmpty_PassesExistingEventTitleToRepository()
    {
        // Arrange
        var article = CreatePendingArticle();
        var lightweightEvent = CreateActiveEvent();
        var enrichedEvent = CreateEnrichedEvent(lightweightEvent.Id);
        const string existingEventTitle = "Existing Event";

        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _eventRepoMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(lightweightEvent, 0.95)]);

        _eventRepoMock
            .Setup(r => r.GetWithContextAsync(lightweightEvent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enrichedEvent);

        _contradictionDetectorMock
            .Setup(d => d.DetectAsync(It.IsAny<Article>(), It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Title generator returns empty — fallback to existing event title should be used
        _titleGeneratorMock
            .Setup(g => g.GenerateTitleAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var sut = CreateWorker();

        // Act
        await RunOneIterationAsync(sut);

        // Assert — the title passed to UpdateSummaryTitleAndEmbeddingAsync must be the event's existing title
        _eventRepoMock.Verify(
            r => r.UpdateSummaryTitleAndEmbeddingAsync(
                enrichedEvent.Id,
                existingEventTitle,
                It.IsAny<string>(),
                It.IsAny<float[]>(),
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
        _articleRepoMock
            .Setup(r => r.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _analyzerMock
            .Setup(a => a.AnalyzeAsync(It.IsAny<Article>(), It.IsAny<CancellationToken>()))
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

    private static Article CreatePendingArticle(List<string>? keyFacts = null) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Contradiction Detection Test Article",
        Status = ArticleStatus.Pending,
        ProcessedAt = DateTimeOffset.UtcNow,
        KeyFacts = keyFacts ?? [],
    };

    private static Event CreateActiveEvent() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Existing Event",
        Summary = "An existing event with known facts.",
        Status = EventStatus.Active,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        Embedding = new float[384],
        ArticleCount = 2,
        EventUpdates = []
    };

    private static Event CreateEnrichedEvent(Guid eventId) =>
        CreateEnrichedEventWithSummary(eventId, "Enriched summary with full context.");

    private static Event CreateEnrichedEventWithSummary(Guid eventId, string summary) => new()
    {
        Id = eventId,
        Title = "Existing Event",
        Summary = summary,
        Status = EventStatus.Active,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        Embedding = new float[384],
        ArticleCount = 2,
        EventUpdates =
        [
            new EventUpdate
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                ArticleId = Guid.NewGuid(),
                FactSummary = "First confirmed fact.",
                CreatedAt = DateTimeOffset.UtcNow,
            }
        ],
        Articles =
        [
            new Article
            {
                Id = Guid.NewGuid(),
                Title = "Original reporting article",
                KeyFacts = ["original key fact"],
                Status = ArticleStatus.AnalysisDone,
                ProcessedAt = DateTimeOffset.UtcNow,
            }
        ],
    };
}
