using Core.DomainModels;
using FluentAssertions;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// EF Core InMemory tests for <see cref="MediaFileRepository"/>.
///
/// <para>
/// Uses the shared <see cref="TestNewsParserDbContext"/> defined in this namespace
/// (see <c>EventRepositoryGetWithContextTests.cs</c>), which overrides
/// <c>OnModelCreating</c> to ignore pgvector properties and keep the EF InMemory
/// provider compatible with the model.
/// </para>
/// </summary>
[TestFixture]
public class MediaFileRepositoryTests
{
    private TestNewsParserDbContext _dbContext = null!;
    private MediaFileRepository _sut = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<NewsParserDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TestNewsParserDbContext(options);
        _dbContext.Database.EnsureCreated();

        _sut = new MediaFileRepository(_dbContext);
    }

    [TearDown]
    public void TearDown() => _dbContext.Dispose();

    // ------------------------------------------------------------------
    // AddAsync — P0: persisted row is retrievable by GetByArticleIdAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task AddAsync_WhenCalled_PersistsRowRetrievableByArticleId()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var mediaFile = CreateMediaFile(articleId, "https://example.com/photo.jpg");

        // Act
        await _sut.AddAsync(mediaFile);

        // Assert
        var result = await _sut.GetByArticleIdAsync(articleId);
        result.Should().HaveCount(1);
        result[0].Id.Should().Be(mediaFile.Id);
        result[0].ArticleId.Should().Be(articleId);
        result[0].OriginalUrl.Should().Be("https://example.com/photo.jpg");
        result[0].Kind.Should().Be(MediaKind.Image);
    }

    // ------------------------------------------------------------------
    // GetByArticleIdAsync — P1: unknown articleId returns empty list
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByArticleIdAsync_WhenNoFilesExistForArticle_ReturnsEmptyList()
    {
        // Arrange
        var unknownArticleId = Guid.NewGuid();

        // Act
        var result = await _sut.GetByArticleIdAsync(unknownArticleId);

        // Assert
        result.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // GetByArticleIdAsync — P2: only files for the matching articleId are returned
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByArticleIdAsync_WhenMultipleArticlesHaveFiles_ReturnsOnlyMatchingArticle()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var otherArticleId = Guid.NewGuid();

        await _sut.AddAsync(CreateMediaFile(articleId, "https://example.com/a.jpg"));
        await _sut.AddAsync(CreateMediaFile(otherArticleId, "https://example.com/b.jpg"));

        // Act
        var result = await _sut.GetByArticleIdAsync(articleId);

        // Assert
        result.Should().HaveCount(1);
        result[0].ArticleId.Should().Be(articleId);
    }

    // ------------------------------------------------------------------
    // ExistsByArticleAndUrlAsync — P0: returns true after insert
    // ------------------------------------------------------------------

    [Test]
    public async Task ExistsByArticleAndUrlAsync_AfterInsert_ReturnsTrue()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        const string url = "https://example.com/media.png";
        await _sut.AddAsync(CreateMediaFile(articleId, url));

        // Act
        var exists = await _sut.ExistsByArticleAndUrlAsync(articleId, url);

        // Assert
        exists.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // ExistsByArticleAndUrlAsync — P1: returns false before any insert
    // ------------------------------------------------------------------

    [Test]
    public async Task ExistsByArticleAndUrlAsync_WhenNoRowExists_ReturnsFalse()
    {
        // Arrange
        var articleId = Guid.NewGuid();

        // Act
        var exists = await _sut.ExistsByArticleAndUrlAsync(articleId, "https://example.com/never-inserted.jpg");

        // Assert
        exists.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // ExistsByArticleAndUrlAsync — P2: same URL under a different articleId returns false
    // ------------------------------------------------------------------

    [Test]
    public async Task ExistsByArticleAndUrlAsync_WhenUrlExistsForDifferentArticle_ReturnsFalse()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var otherArticleId = Guid.NewGuid();
        const string url = "https://example.com/shared.jpg";

        await _sut.AddAsync(CreateMediaFile(otherArticleId, url));

        // Act
        var exists = await _sut.ExistsByArticleAndUrlAsync(articleId, url);

        // Assert
        exists.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static MediaFile CreateMediaFile(Guid articleId, string originalUrl) => new()
    {
        Id = Guid.NewGuid(),
        ArticleId = articleId,
        R2Key = $"articles/{articleId}/{Guid.NewGuid()}.jpg",
        OriginalUrl = originalUrl,
        ContentType = "image/jpeg",
        SizeBytes = 102_400,
        Kind = MediaKind.Image,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
