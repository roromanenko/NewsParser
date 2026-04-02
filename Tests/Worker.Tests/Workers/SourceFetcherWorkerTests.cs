using Core.DomainModels;
using Core.Interfaces.Parsers;
using Core.Interfaces.Repositories;
using Core.Interfaces.Validators;
using Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Worker.Configuration;
using Worker.Workers;

namespace Worker.Tests.Workers;

[TestFixture]
public class SourceFetcherWorkerTests
{
	private Mock<ISourceRepository> _sourceRepoMock = null!;
	private Mock<IRawArticleRepository> _rawArticleRepoMock = null!;
	private Mock<IRawArticleValidator> _validatorMock = null!;
	private Mock<ISourceParser> _parserMock = null!;
	private Mock<IServiceScopeFactory> _scopeFactoryMock = null!;

	private Source _testSource = null!;
	private IOptions<RssFetcherOptions> _rssFetcherOptions = null!;
	private IOptions<ValidationOptions> _validationOptions = null!;

	[SetUp]
	public void SetUp()
	{
		_sourceRepoMock = new Mock<ISourceRepository>();
		_rawArticleRepoMock = new Mock<IRawArticleRepository>();
		_validatorMock = new Mock<IRawArticleValidator>();
		_parserMock = new Mock<ISourceParser>();
		_scopeFactoryMock = new Mock<IServiceScopeFactory>();

		// Use a very long interval so the loop only completes one iteration before cancellation
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

		// Default: no recent titles, no existing external-id, no existing URL
		_rawArticleRepoMock
			.Setup(r => r.GetRecentTitlesForDeduplicationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([]);

		_rawArticleRepoMock
			.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(false);

		_rawArticleRepoMock
			.Setup(r => r.ExistsByUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(false);

		_validatorMock
			.Setup(v => v.Validate(It.IsAny<RawArticle>()))
			.Returns((true, (string?)null));

		WireUpScopeFactory();
	}

	// ------------------------------------------------------------------
	// Scenario 1: ExternalId already exists → AddAsync never called
	// ------------------------------------------------------------------
	[Test]
	public async Task ProcessSourceAsync_WhenExternalIdAlreadyExists_DoesNotAddArticle()
	{
		// Arrange
		var article = CreateArticle(externalId: "existing-id-1");
		_parserMock
			.Setup(p => p.ParseAsync(_testSource, It.IsAny<CancellationToken>()))
			.ReturnsAsync([article]);

		_rawArticleRepoMock
			.Setup(r => r.ExistsAsync(_testSource.Id, "existing-id-1", It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);

		var sut = CreateWorker();

		// Act
		await RunOneIterationAsync(sut);

		// Assert
		_rawArticleRepoMock.Verify(r => r.AddAsync(It.IsAny<RawArticle>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	// ------------------------------------------------------------------
	// Scenario 2: URL already exists (cross-source) → AddAsync never called
	// ------------------------------------------------------------------
	[Test]
	public async Task ProcessSourceAsync_WhenUrlAlreadyExistsInAnotherSource_DoesNotAddArticle()
	{
		// Arrange
		var article = CreateArticle(externalId: "unique-id-2", url: "https://news.com/duplicate");
		_parserMock
			.Setup(p => p.ParseAsync(_testSource, It.IsAny<CancellationToken>()))
			.ReturnsAsync([article]);

		_rawArticleRepoMock
			.Setup(r => r.ExistsByUrlAsync("https://news.com/duplicate", It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);

		var sut = CreateWorker();

		// Act
		await RunOneIterationAsync(sut);

		// Assert
		_rawArticleRepoMock.Verify(r => r.AddAsync(It.IsAny<RawArticle>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	// ------------------------------------------------------------------
	// Scenario 3: Title fuzzy match >= threshold → AddAsync never called
	// ------------------------------------------------------------------
	[Test]
	public async Task ProcessSourceAsync_WhenTitleFuzzyScoreAtOrAboveThreshold_DoesNotAddArticle()
	{
		// Arrange — title and existing title that FuzzySharp will score >= 85
		const string incomingTitle = "Breaking: Major earthquake hits region";
		const string existingTitle = "Breaking: Major earthquake hits region today";

		var article = CreateArticle(externalId: "unique-id-3", title: incomingTitle);
		_parserMock
			.Setup(p => p.ParseAsync(_testSource, It.IsAny<CancellationToken>()))
			.ReturnsAsync([article]);

		_rawArticleRepoMock
			.Setup(r => r.GetRecentTitlesForDeduplicationAsync(24, It.IsAny<CancellationToken>()))
			.ReturnsAsync([existingTitle]);

		var sut = CreateWorker();

		// Act
		await RunOneIterationAsync(sut);

		// Assert — score between these two titles is well above 85
		_rawArticleRepoMock.Verify(r => r.AddAsync(It.IsAny<RawArticle>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	// ------------------------------------------------------------------
	// Scenario 4: Title fuzzy match < threshold → AddAsync called once
	// ------------------------------------------------------------------
	[Test]
	public async Task ProcessSourceAsync_WhenTitleFuzzyScoreBelowThreshold_AddsArticle()
	{
		// Arrange — titles are semantically unrelated; FuzzySharp score will be well below 85
		const string incomingTitle = "Stock markets rally amid economic optimism";
		const string existingTitle = "Polar bears spotted near northern coastline";

		var article = CreateArticle(externalId: "unique-id-4", title: incomingTitle);
		_parserMock
			.Setup(p => p.ParseAsync(_testSource, It.IsAny<CancellationToken>()))
			.ReturnsAsync([article]);

		_rawArticleRepoMock
			.Setup(r => r.GetRecentTitlesForDeduplicationAsync(24, It.IsAny<CancellationToken>()))
			.ReturnsAsync([existingTitle]);

		var sut = CreateWorker();

		// Act
		await RunOneIterationAsync(sut);

		// Assert
		_rawArticleRepoMock.Verify(r => r.AddAsync(article, It.IsAny<CancellationToken>()), Times.Once);
	}

	// ------------------------------------------------------------------
	// Scenario 5: No recent titles → AddAsync called once
	// ------------------------------------------------------------------
	[Test]
	public async Task ProcessSourceAsync_WhenNoRecentTitlesExist_AddsArticle()
	{
		// Arrange
		var article = CreateArticle(externalId: "unique-id-5");
		_parserMock
			.Setup(p => p.ParseAsync(_testSource, It.IsAny<CancellationToken>()))
			.ReturnsAsync([article]);

		// Default setup already returns empty recent titles

		var sut = CreateWorker();

		// Act
		await RunOneIterationAsync(sut);

		// Assert
		_rawArticleRepoMock.Verify(r => r.AddAsync(article, It.IsAny<CancellationToken>()), Times.Once);
	}

	// ------------------------------------------------------------------
	// Scenario 6: Invalid article (validator returns false) → AddAsync never called
	// ------------------------------------------------------------------
	[Test]
	public async Task ProcessSourceAsync_WhenValidatorRejectsArticle_DoesNotAddArticle()
	{
		// Arrange
		var article = CreateArticle(externalId: "unique-id-6");
		_parserMock
			.Setup(p => p.ParseAsync(_testSource, It.IsAny<CancellationToken>()))
			.ReturnsAsync([article]);

		_validatorMock
			.Setup(v => v.Validate(article))
			.Returns((false, "Content too short"));

		var sut = CreateWorker();

		// Act
		await RunOneIterationAsync(sut);

		// Assert
		_rawArticleRepoMock.Verify(r => r.AddAsync(It.IsAny<RawArticle>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	// ------------------------------------------------------------------
	// Scenario 7: Empty ExternalId → ExistsAsync never called (article skipped before any repo call)
	// ------------------------------------------------------------------
	[Test]
	public async Task ProcessSourceAsync_WhenExternalIdIsEmpty_SkipsArticleWithoutCallingExistsAsync()
	{
		// Arrange
		var article = CreateArticle(externalId: string.Empty);
		_parserMock
			.Setup(p => p.ParseAsync(_testSource, It.IsAny<CancellationToken>()))
			.ReturnsAsync([article]);

		var sut = CreateWorker();

		// Act
		await RunOneIterationAsync(sut);

		// Assert
		_rawArticleRepoMock.Verify(
			r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
			Times.Never);
		_rawArticleRepoMock.Verify(r => r.AddAsync(It.IsAny<RawArticle>(), It.IsAny<CancellationToken>()), Times.Never);
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

	/// <summary>
	/// Starts the worker, lets it process one full iteration, then stops it.
	/// The Task.Delay(9999s) in the loop will be cancelled by StopAsync, so the
	/// worker exits without waiting.
	/// </summary>
	private static async Task RunOneIterationAsync(SourceFetcherWorker sut)
	{
		using var cts = new CancellationTokenSource();

		await sut.StartAsync(cts.Token);

		// Give the background task time to run one full iteration
		await Task.Delay(300);

		await sut.StopAsync(CancellationToken.None);
	}

	private void WireUpScopeFactory()
	{
		var scopeMock = new Mock<IServiceScope>();
		var serviceProviderMock = new Mock<IServiceProvider>();

		serviceProviderMock
			.Setup(sp => sp.GetService(typeof(ISourceRepository)))
			.Returns(_sourceRepoMock.Object);

		serviceProviderMock
			.Setup(sp => sp.GetService(typeof(IRawArticleRepository)))
			.Returns(_rawArticleRepoMock.Object);

		serviceProviderMock
			.Setup(sp => sp.GetService(typeof(IRawArticleValidator)))
			.Returns(_validatorMock.Object);

		// GetServices<ISourceParser>() is resolved via IEnumerable<ISourceParser>
		serviceProviderMock
			.Setup(sp => sp.GetService(typeof(IEnumerable<ISourceParser>)))
			.Returns(new[] { _parserMock.Object });

		scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
		_scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
	}

	private static RawArticle CreateArticle(
		string externalId,
		string title = "Test Article Title For Testing",
		string url = "https://example.com/article") =>
		new()
		{
			Id = Guid.NewGuid(),
			ExternalId = externalId,
			Title = title,
			OriginalUrl = url,
			Content = "Some article content long enough to pass validation",
			PublishedAt = DateTimeOffset.UtcNow
		};
}
