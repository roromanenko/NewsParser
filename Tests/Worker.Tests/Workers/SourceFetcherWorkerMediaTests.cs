using Core.DomainModels;
using Core.Interfaces.Parsers;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Core.Interfaces.Validators;
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
/// Focused tests verifying that <see cref="SourceFetcherWorker"/> integrates with
/// <see cref="IMediaIngestionService"/> correctly:
/// - When ingestion throws, the article loop continues and the saved count is unaffected.
/// - When ingestion succeeds, the article is still saved and saved++ is incremented.
/// - Telegram articles with MediaReferences trigger ingestion the same way RSS articles do.
/// </summary>
[TestFixture]
public class SourceFetcherWorkerMediaTests
{
    private Mock<ISourceRepository> _sourceRepoMock = null!;
    private Mock<IArticleRepository> _articleRepoMock = null!;
    private Mock<IArticleValidator> _validatorMock = null!;
    private Mock<ISourceParser> _parserMock = null!;
    private Mock<IMediaIngestionService> _mediaIngestionServiceMock = null!;
    private Mock<IServiceScopeFactory> _scopeFactoryMock = null!;

    private Source _testSource = null!;
    private IOptions<RssFetcherOptions> _rssFetcherOptions = null!;
    private IOptions<ValidationOptions> _validationOptions = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepoMock = new Mock<ISourceRepository>();
        _articleRepoMock = new Mock<IArticleRepository>();
        _validatorMock = new Mock<IArticleValidator>();
        _parserMock = new Mock<ISourceParser>();
        _mediaIngestionServiceMock = new Mock<IMediaIngestionService>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();

        _rssFetcherOptions = Options.Create(new RssFetcherOptions { IntervalSeconds = 9999 });
        _validationOptions = Options.Create(new ValidationOptions
        {
            TitleSimilarityThreshold = 85,
            TitleDeduplicationWindowHours = 24
        });

        _testSource = new Source
        {
            Id = Guid.NewGuid(),
            Name = "Test Source",
            Url = "https://example.com/rss",
            Type = SourceType.Rss,
            IsActive = true
        };

        _parserMock.Setup(p => p.SourceType).Returns(SourceType.Rss);

        _sourceRepoMock
            .Setup(r => r.GetActiveAsync(SourceType.Rss, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_testSource]);

        _articleRepoMock
            .Setup(r => r.GetRecentTitlesForDeduplicationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _articleRepoMock
            .Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _articleRepoMock
            .Setup(r => r.ExistsByUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _validatorMock
            .Setup(v => v.Validate(It.IsAny<Article>()))
            .Returns((true, (string?)null));

        // Default: ingestion succeeds
        _mediaIngestionServiceMock
            .Setup(s => s.IngestForArticleAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<MediaReference>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        WireUpScopeFactory([_parserMock.Object]);
    }

    // ------------------------------------------------------------------
    // P0 — When IngestForArticleAsync throws, the loop continues and
    //       AddAsync is still called for that article (saved++ unaffected)
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessSourceAsync_WhenMediaIngestionThrows_ArticleIsStillSavedAndLoopContinues()
    {
        // Arrange
        var article = CreateArticle("article-1", "Earthquake Hits Central America");
        _parserMock
            .Setup(p => p.ParseAsync(_testSource, It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _mediaIngestionServiceMock
            .Setup(s => s.IngestForArticleAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<MediaReference>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected ingestion error"));

        var sut = CreateWorker();

        // Act — must not throw even though ingestion throws
        await RunOneIterationAsync(sut);

        // Assert — article was still saved despite ingestion failure
        _articleRepoMock.Verify(r => r.AddAsync(article, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // P0 — When IngestForArticleAsync succeeds, AddAsync is called and
    //       ingestion is triggered for the saved article's Id
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessSourceAsync_WhenMediaIngestionSucceeds_ArticleSavedAndIngestionCalledWithArticleId()
    {
        // Arrange
        var article = CreateArticle("article-2", "Stock Markets Rally on Positive Data");
        _parserMock
            .Setup(p => p.ParseAsync(_testSource, It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        // Act
        await RunOneIterationAsync(CreateWorker());

        // Assert
        _articleRepoMock.Verify(r => r.AddAsync(article, It.IsAny<CancellationToken>()), Times.Once);
        _mediaIngestionServiceMock.Verify(
            s => s.IngestForArticleAsync(article.Id, article.MediaReferences, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P1 — When one article's ingestion throws, subsequent articles
    //       in the same batch are still processed
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessSourceAsync_WhenFirstArticleIngestionThrows_SecondArticleIsStillSaved()
    {
        // Arrange — use distinct, unrelated titles so intra-batch fuzzy dedup does not filter
        // the second article after the first is saved (worker adds first title to recentTitles)
        var firstArticle = CreateArticle("article-3a", "Polar Bears Spotted Near Arctic Coast");
        var secondArticle = CreateArticle("article-3b", "Tech IPO Raises Record Billion Dollars");

        _parserMock
            .Setup(p => p.ParseAsync(_testSource, It.IsAny<CancellationToken>()))
            .ReturnsAsync([firstArticle, secondArticle]);

        _mediaIngestionServiceMock
            .Setup(s => s.IngestForArticleAsync(firstArticle.Id, It.IsAny<IReadOnlyList<MediaReference>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("ingestion failed for first"));

        _mediaIngestionServiceMock
            .Setup(s => s.IngestForArticleAsync(secondArticle.Id, It.IsAny<IReadOnlyList<MediaReference>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await RunOneIterationAsync(CreateWorker());

        // Assert — both articles saved despite first ingestion failure
        _articleRepoMock.Verify(r => r.AddAsync(firstArticle, It.IsAny<CancellationToken>()), Times.Once);
        _articleRepoMock.Verify(r => r.AddAsync(secondArticle, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // P0 — Telegram article with one photo MediaReference: after save,
    //       IngestForArticleAsync is called once with that article's Id
    //       and the Telegram MediaReference list
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessSourceAsync_WhenTelegramArticleHasPhotoReference_IngestionCalledWithTelegramReference()
    {
        // Arrange
        var telegramSource = new Source
        {
            Id = Guid.NewGuid(),
            Name = "My Telegram Channel",
            Url = "https://t.me/mychannel",
            Type = SourceType.Telegram,
            IsActive = true
        };

        var telegramParserMock = new Mock<ISourceParser>();
        telegramParserMock.Setup(p => p.SourceType).Returns(SourceType.Telegram);

        _sourceRepoMock
            .Setup(r => r.GetActiveAsync(SourceType.Telegram, It.IsAny<CancellationToken>()))
            .ReturnsAsync([telegramSource]);

        var telegramMediaRef = new MediaReference(
            "https://t.me/mychannel/10#media-0",
            MediaKind.Image,
            "image/jpeg",
            MediaSourceKind.Telegram,
            "100:999:10:0");

        var article = CreateTelegramArticle("tg-10", "Breaking: Telegram Article With Photo", telegramMediaRef);

        telegramParserMock
            .Setup(p => p.ParseAsync(telegramSource, It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        WireUpScopeFactory([telegramParserMock.Object]);

        // Act
        await RunOneIterationAsync(CreateWorker());

        // Assert
        _articleRepoMock.Verify(r => r.AddAsync(article, It.IsAny<CancellationToken>()), Times.Once);
        _mediaIngestionServiceMock.Verify(
            s => s.IngestForArticleAsync(
                article.Id,
                It.Is<IReadOnlyList<MediaReference>>(refs =>
                    refs.Count == 1
                    && refs[0].SourceKind == MediaSourceKind.Telegram
                    && refs[0].Url == telegramMediaRef.Url),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P1 — Telegram article: IngestForArticleAsync throws → article is still
    //       saved and the loop does not abort (best-effort contract preserved)
    // ------------------------------------------------------------------

    [Test]
    public async Task ProcessSourceAsync_WhenTelegramIngestionThrows_ArticleIsStillSavedAndLoopContinues()
    {
        // Arrange
        var telegramSource = new Source
        {
            Id = Guid.NewGuid(),
            Name = "Telegram News",
            Url = "https://t.me/news",
            Type = SourceType.Telegram,
            IsActive = true
        };

        var telegramParserMock = new Mock<ISourceParser>();
        telegramParserMock.Setup(p => p.SourceType).Returns(SourceType.Telegram);

        _sourceRepoMock
            .Setup(r => r.GetActiveAsync(SourceType.Telegram, It.IsAny<CancellationToken>()))
            .ReturnsAsync([telegramSource]);

        var telegramMediaRef = new MediaReference(
            "https://t.me/news/5#media-0",
            MediaKind.Image,
            "image/jpeg",
            MediaSourceKind.Telegram,
            "200:888:5:0");

        var article = CreateTelegramArticle("tg-5", "Satellite Launch Succeeds After Delay", telegramMediaRef);

        telegramParserMock
            .Setup(p => p.ParseAsync(telegramSource, It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        _mediaIngestionServiceMock
            .Setup(s => s.IngestForArticleAsync(article.Id, It.IsAny<IReadOnlyList<MediaReference>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Telegram ingestion failed"));

        WireUpScopeFactory([telegramParserMock.Object]);

        // Act — must not throw
        var act = async () => await RunOneIterationAsync(CreateWorker());

        // Assert
        await act.Should().NotThrowAsync();
        _articleRepoMock.Verify(r => r.AddAsync(article, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private SourceFetcherWorker CreateWorker() =>
        new(
            _scopeFactoryMock.Object,
            NullLogger<SourceFetcherWorker>.Instance,
            _rssFetcherOptions,
            _validationOptions);

    private static async Task RunOneIterationAsync(SourceFetcherWorker sut)
    {
        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await Task.Delay(300);
        await sut.StopAsync(CancellationToken.None);
    }

    private void WireUpScopeFactory(ISourceParser[] parsers)
    {
        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ISourceRepository)))
            .Returns(_sourceRepoMock.Object);

        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IArticleRepository)))
            .Returns(_articleRepoMock.Object);

        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IArticleValidator)))
            .Returns(_validatorMock.Object);

        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IMediaIngestionService)))
            .Returns(_mediaIngestionServiceMock.Object);

        // GetServices<ISourceParser>() is resolved via IEnumerable<ISourceParser>
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IEnumerable<ISourceParser>)))
            .Returns(parsers);

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
    }

    private static Article CreateArticle(string externalId, string title) => new()
    {
        Id = Guid.NewGuid(),
        ExternalId = externalId,
        Title = title,
        OriginalUrl = $"https://example.com/{externalId}",
        OriginalContent = "Some article content long enough to pass validation",
        PublishedAt = DateTimeOffset.UtcNow,
        MediaReferences = [],
    };

    private static Article CreateTelegramArticle(string externalId, string title, MediaReference mediaRef) => new()
    {
        Id = Guid.NewGuid(),
        ExternalId = externalId,
        Title = title,
        OriginalUrl = $"https://t.me/channel/{externalId}",
        OriginalContent = "Telegram article content long enough to pass validation checks",
        PublishedAt = DateTimeOffset.UtcNow,
        MediaReferences = [mediaRef],
    };
}
