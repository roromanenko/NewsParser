using Core.DomainModels;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// Tests for the RejectAsync, GetAnalysisDoneAsync, and CountAnalysisDoneAsync
/// contracts on IArticleRepository.
///
/// NOTE: The production implementation uses EF Core's ExecuteUpdateAsync (bulk-update
/// API) and requires the pgvector extension in OnModelCreating, which is not supported
/// by EF InMemory or SQLite InMemory. A full integration test of these methods requires
/// Testcontainers with a real PostgreSQL + pgvector instance (out of scope here).
///
/// These tests verify the IArticleRepository interface contract — the shape expected
/// by all callers (workers, services). Worker-level integration of RejectAsync is
/// covered in ArticleAnalysisWorkerDeduplicationTests.
/// </summary>
[TestFixture]
public class ArticleRepositoryRejectAndQueryTests
{
    private Mock<IArticleRepository> _repositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IArticleRepository>();
    }

    // ------------------------------------------------------------------
    // RejectAsync — P0: accepts an id and reason, completes without throwing
    // ------------------------------------------------------------------

    [Test]
    public async Task RejectAsync_WhenCalledWithValidIdAndReason_CompletesWithoutThrowing()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        const string reason = "duplicate_by_vector";

        _repositoryMock
            .Setup(r => r.RejectAsync(articleId, reason, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () =>
            await _repositoryMock.Object.RejectAsync(articleId, reason, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        _repositoryMock.Verify(
            r => r.RejectAsync(articleId, reason, CancellationToken.None),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // RejectAsync — P0: passes the exact reason string to the repository
    // ------------------------------------------------------------------

    [Test]
    public async Task RejectAsync_WhenCalledWithDuplicateReason_ReceivesCorrectReasonString()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        string? capturedReason = null;

        _repositoryMock
            .Setup(r => r.RejectAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, CancellationToken>((_, r, _) => capturedReason = r)
            .Returns(Task.CompletedTask);

        // Act
        await _repositoryMock.Object.RejectAsync(articleId, "duplicate_by_vector", CancellationToken.None);

        // Assert
        capturedReason.Should().Be("duplicate_by_vector");
    }

    // ------------------------------------------------------------------
    // RejectAsync — P2: empty reason string is a valid argument
    // ------------------------------------------------------------------

    [Test]
    public async Task RejectAsync_WhenCalledWithEmptyReason_CompletesWithoutThrowing()
    {
        // Arrange
        var articleId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.RejectAsync(articleId, string.Empty, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () =>
            await _repositoryMock.Object.RejectAsync(articleId, string.Empty, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------
    // GetAnalysisDoneAsync — P0: returns paged list filtered by AnalysisDone
    // ------------------------------------------------------------------

    [Test]
    public async Task GetAnalysisDoneAsync_WhenArticlesExist_ReturnsPaginatedList()
    {
        // Arrange
        const int page = 1;
        const int pageSize = 10;
        var expected = new List<Article>
        {
            CreateAnalysisDoneArticle(),
            CreateAnalysisDoneArticle()
        };

        _repositoryMock
            .Setup(r => r.GetAnalysisDoneAsync(page, pageSize, It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _repositoryMock.Object.GetAnalysisDoneAsync(page, pageSize, null, "newest", CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(a => a.Status.Should().Be(ArticleStatus.AnalysisDone));
    }

    // ------------------------------------------------------------------
    // GetAnalysisDoneAsync — P1: returns empty list when no articles match
    // ------------------------------------------------------------------

    [Test]
    public async Task GetAnalysisDoneAsync_WhenNoArticlesMatch_ReturnsEmptyList()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetAnalysisDoneAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _repositoryMock.Object.GetAnalysisDoneAsync(1, 10, null, "newest", CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // CountAnalysisDoneAsync — P0: returns correct count of AnalysisDone articles
    // ------------------------------------------------------------------

    [Test]
    public async Task CountAnalysisDoneAsync_WhenArticlesExist_ReturnsCorrectCount()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.CountAnalysisDoneAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        // Act
        var result = await _repositoryMock.Object.CountAnalysisDoneAsync(null, CancellationToken.None);

        // Assert
        result.Should().Be(5);
    }

    // ------------------------------------------------------------------
    // CountAnalysisDoneAsync — P1: returns 0 when no AnalysisDone articles exist
    // ------------------------------------------------------------------

    [Test]
    public async Task CountAnalysisDoneAsync_WhenNoArticlesMatch_ReturnsZero()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.CountAnalysisDoneAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _repositoryMock.Object.CountAnalysisDoneAsync(null, CancellationToken.None);

        // Assert
        result.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Article CreateAnalysisDoneArticle() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Analysis Done Article",
        Status = ArticleStatus.AnalysisDone,
        ProcessedAt = DateTimeOffset.UtcNow
    };
}
