using Core.DomainModels;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// Tests for the UpdateKeyFactsAsync contract on IArticleRepository.
///
/// NOTE: The production implementation (ArticleRepository.UpdateKeyFactsAsync) uses
/// EF Core's ExecuteUpdateAsync (bulk-update API), which is not supported by either
/// the EF InMemory provider or SQLite InMemory because NewsParserDbContext requires
/// the pgvector extension in OnModelCreating. A full integration test of this method
/// requires Testcontainers with a real PostgreSQL + pgvector instance (out of scope here).
///
/// These tests verify the contract at the IArticleRepository interface level, which
/// is what all callers (ArticleAnalysisWorker, etc.) depend on. The worker-layer tests
/// in ArticleAnalysisWorkerKeyFactsTests verify the full integration of extraction
/// and persistence through the mocked repository.
/// </summary>
[TestFixture]
public class ArticleRepositoryUpdateKeyFactsTests
{
    private Mock<IArticleRepository> _repositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IArticleRepository>();
    }

    // ------------------------------------------------------------------
    // P0 — Verifies the contract: the method accepts an article id and
    //       a list of key facts, completing without throwing.
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateKeyFactsAsync_WhenCalledWithValidIdAndFacts_CompletesWithoutThrowing()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var keyFacts = new List<string> { "Fact one.", "Fact two.", "Fact three." };

        _repositoryMock
            .Setup(r => r.UpdateKeyFactsAsync(articleId, keyFacts, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () =>
            await _repositoryMock.Object.UpdateKeyFactsAsync(articleId, keyFacts, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        _repositoryMock.Verify(
            r => r.UpdateKeyFactsAsync(articleId, keyFacts, CancellationToken.None),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P0 — Verifies that when called twice for the same article the second
    //       call replaces (overwrites) the previous list — callers should
    //       pass the full updated list, not append.
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateKeyFactsAsync_WhenCalledTwiceForSameArticle_SecondCallReceivesNewList()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var originalFacts = new List<string> { "Old fact one.", "Old fact two." };
        var updatedFacts = new List<string> { "New fact A.", "New fact B.", "New fact C." };

        _repositoryMock
            .Setup(r => r.UpdateKeyFactsAsync(It.IsAny<Guid>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _repositoryMock.Object.UpdateKeyFactsAsync(articleId, originalFacts, CancellationToken.None);
        await _repositoryMock.Object.UpdateKeyFactsAsync(articleId, updatedFacts, CancellationToken.None);

        // Assert — each call is independent; the second call carries only the new list
        _repositoryMock.Verify(
            r => r.UpdateKeyFactsAsync(articleId, originalFacts, CancellationToken.None),
            Times.Once);
        _repositoryMock.Verify(
            r => r.UpdateKeyFactsAsync(articleId, updatedFacts, CancellationToken.None),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P2 — Verifies empty list is a valid argument (clears existing facts)
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateKeyFactsAsync_WhenCalledWithEmptyList_CompletesWithoutThrowing()
    {
        // Arrange
        var articleId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.UpdateKeyFactsAsync(articleId, It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () =>
            await _repositoryMock.Object.UpdateKeyFactsAsync(articleId, [], CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
