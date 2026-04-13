using Core.DomainModels;
using FluentAssertions;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// Tests for the search/sort/count behaviour introduced by the server-side
/// pagination feature on <see cref="ArticleRepository"/>.
///
/// EF InMemory does not support PostgreSQL's ILike function, so search tests
/// verify the non-search path (null / empty search) and ordering behaviour.
/// The ILike code path is exercised by integration tests against a real
/// PostgreSQL instance.
/// </summary>
[TestFixture]
public class ArticleRepositorySearchSortTests
{
    private TestNewsParserDbContext _db = null!;
    private ArticleRepository _sut = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<NewsParserDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new TestNewsParserDbContext(options);
        _sut = new ArticleRepository(_db);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    // ------------------------------------------------------------------
    // GetAnalysisDoneAsync — P0: null search returns all AnalysisDone articles
    // ------------------------------------------------------------------

    [Test]
    public async Task GetAnalysisDoneAsync_WhenSearchIsNull_ReturnsAllAnalysisDoneArticles()
    {
        // Arrange
        var analysisDone1 = CreateArticleEntity("Article One", ArticleStatus.AnalysisDone, daysOld: 2);
        var analysisDone2 = CreateArticleEntity("Article Two", ArticleStatus.AnalysisDone, daysOld: 1);
        var rejected = CreateArticleEntity("Rejected Article", ArticleStatus.Rejected, daysOld: 1);

        _db.Articles.AddRange(analysisDone1, analysisDone2, rejected);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetAnalysisDoneAsync(page: 1, pageSize: 10, search: null, sortBy: "newest");

        // Assert
        result.Should().HaveCount(2);
        result.Should().NotContain(a => a.Title == "Rejected Article");
    }

    // ------------------------------------------------------------------
    // GetAnalysisDoneAsync — P0: sortBy="oldest" orders by ProcessedAt ASC
    // ------------------------------------------------------------------

    [Test]
    public async Task GetAnalysisDoneAsync_WhenSortByIsOldest_ReturnsArticlesOrderedByProcessedAtAscending()
    {
        // Arrange
        var older = CreateArticleEntity("Older Article", ArticleStatus.AnalysisDone, daysOld: 5);
        var newer = CreateArticleEntity("Newer Article", ArticleStatus.AnalysisDone, daysOld: 1);

        _db.Articles.AddRange(older, newer);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetAnalysisDoneAsync(page: 1, pageSize: 10, search: null, sortBy: "oldest");

        // Assert
        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Older Article");
        result[1].Title.Should().Be("Newer Article");
    }

    // ------------------------------------------------------------------
    // GetAnalysisDoneAsync — P0: sortBy="newest" orders by ProcessedAt DESC
    // ------------------------------------------------------------------

    [Test]
    public async Task GetAnalysisDoneAsync_WhenSortByIsNewest_ReturnsArticlesOrderedByProcessedAtDescending()
    {
        // Arrange
        var older = CreateArticleEntity("Older Article", ArticleStatus.AnalysisDone, daysOld: 5);
        var newer = CreateArticleEntity("Newer Article", ArticleStatus.AnalysisDone, daysOld: 1);

        _db.Articles.AddRange(older, newer);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetAnalysisDoneAsync(page: 1, pageSize: 10, search: null, sortBy: "newest");

        // Assert
        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Newer Article");
        result[1].Title.Should().Be("Older Article");
    }

    // ------------------------------------------------------------------
    // GetAnalysisDoneAsync — P1: unknown sortBy falls back to DESC order
    // ------------------------------------------------------------------

    [Test]
    public async Task GetAnalysisDoneAsync_WhenSortByIsUnknownValue_FallsBackToDescendingOrder()
    {
        // Arrange
        var older = CreateArticleEntity("Older Article", ArticleStatus.AnalysisDone, daysOld: 5);
        var newer = CreateArticleEntity("Newer Article", ArticleStatus.AnalysisDone, daysOld: 1);

        _db.Articles.AddRange(older, newer);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetAnalysisDoneAsync(page: 1, pageSize: 10, search: null, sortBy: "invalid_sort");

        // Assert
        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Newer Article", "unknown sortBy must fall back to newest-first (DESC)");
        result[1].Title.Should().Be("Older Article");
    }

    // ------------------------------------------------------------------
    // CountAnalysisDoneAsync — P0: null search counts all AnalysisDone articles
    // ------------------------------------------------------------------

    [Test]
    public async Task CountAnalysisDoneAsync_WhenSearchIsNull_CountsAllAnalysisDoneArticles()
    {
        // Arrange
        var analysisDone1 = CreateArticleEntity("Article One", ArticleStatus.AnalysisDone, daysOld: 2);
        var analysisDone2 = CreateArticleEntity("Article Two", ArticleStatus.AnalysisDone, daysOld: 1);
        var rejected = CreateArticleEntity("Rejected Article", ArticleStatus.Rejected, daysOld: 1);

        _db.Articles.AddRange(analysisDone1, analysisDone2, rejected);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        var count = await _sut.CountAnalysisDoneAsync(search: null);

        // Assert
        count.Should().Be(2);
    }

    // ------------------------------------------------------------------
    // CountAnalysisDoneAsync — P1: excludes articles with other statuses
    // ------------------------------------------------------------------

    [Test]
    public async Task CountAnalysisDoneAsync_WhenOnlyNonAnalysisDoneArticlesExist_ReturnsZero()
    {
        // Arrange
        var pending = CreateArticleEntity("Pending Article", ArticleStatus.Pending, daysOld: 1);
        var analyzing = CreateArticleEntity("Analyzing Article", ArticleStatus.Analyzing, daysOld: 1);

        _db.Articles.AddRange(pending, analyzing);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        var count = await _sut.CountAnalysisDoneAsync(search: null);

        // Assert
        count.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static ArticleEntity CreateArticleEntity(
        string title,
        ArticleStatus status,
        int daysOld) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Status = status.ToString(),
        Sentiment = "Neutral",
        Category = "Tech",
        Language = "en",
        ModelVersion = "claude-3-haiku",
        ProcessedAt = DateTimeOffset.UtcNow.AddDays(-daysOld),
        Tags = [],
        KeyFacts = [],
    };
}
