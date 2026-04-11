using Core.DomainModels;
using FluentAssertions;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// EF Core InMemory tests verifying that <see cref="ArticleRepository.GetByIdAsync"/>
/// eagerly loads <c>MediaFiles</c> via <c>Include</c>, and that
/// <see cref="ArticleRepository.GetAnalysisDoneAsync"/> does NOT load media rows
/// (regression guard — media must not appear in list queries).
///
/// Uses <see cref="TestNewsParserDbContext"/> (defined in
/// <c>EventRepositoryGetWithContextTests.cs</c>) which strips pgvector columns and
/// wires up all navigation properties for EF InMemory.
/// </summary>
[TestFixture]
public class ArticleRepositoryGetByIdWithMediaTests
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
    // GetByIdAsync — P0: returns both MediaFile rows when two exist
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByIdAsync_WhenArticleHasTwoMediaFiles_ReturnsBothInMediaFiles()
    {
        // Arrange
        var articleEntity = CreateArticleEntity();
        var media1 = CreateMediaFileEntity(articleEntity.Id, r2Key: "articles/img1.jpg");
        var media2 = CreateMediaFileEntity(articleEntity.Id, r2Key: "articles/img2.png");

        _db.Articles.Add(articleEntity);
        _db.MediaFiles.Add(media1);
        _db.MediaFiles.Add(media2);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetByIdAsync(articleEntity.Id);

        // Assert
        result.Should().NotBeNull();
        result!.MediaFiles.Should().HaveCount(2);
        result.MediaFiles.Should().ContainSingle(m => m.R2Key == "articles/img1.jpg");
        result.MediaFiles.Should().ContainSingle(m => m.R2Key == "articles/img2.png");
    }

    // ------------------------------------------------------------------
    // GetByIdAsync — P1: article with no media returns empty MediaFiles list
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByIdAsync_WhenArticleHasNoMediaFiles_ReturnsEmptyMediaFilesList()
    {
        // Arrange
        var articleEntity = CreateArticleEntity();

        _db.Articles.Add(articleEntity);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetByIdAsync(articleEntity.Id);

        // Assert
        result.Should().NotBeNull();
        result!.MediaFiles.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // GetByIdAsync — P1: returns null for unknown id
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByIdAsync_WhenArticleDoesNotExist_ReturnsNull()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        // Act
        var result = await _sut.GetByIdAsync(unknownId);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // GetAnalysisDoneAsync — P0 (regression guard): does NOT populate MediaFiles
    // ------------------------------------------------------------------

    [Test]
    public async Task GetAnalysisDoneAsync_WhenArticleHasMediaFiles_DoesNotPopulateMediaFilesCollection()
    {
        // Arrange
        var articleEntity = CreateArticleEntity(status: ArticleStatus.AnalysisDone.ToString());
        var media = CreateMediaFileEntity(articleEntity.Id, r2Key: "articles/img.jpg");

        _db.Articles.Add(articleEntity);
        _db.MediaFiles.Add(media);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();

        // Act
        var results = await _sut.GetAnalysisDoneAsync(page: 1, pageSize: 10);

        // Assert
        results.Should().HaveCount(1);
        results[0].MediaFiles.Should().BeEmpty(
            "GetAnalysisDoneAsync does not Include MediaFiles — media must not leak into list queries");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static ArticleEntity CreateArticleEntity(string? status = null) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Article",
        Status = status ?? ArticleStatus.AnalysisDone.ToString(),
        Sentiment = "Neutral",
        Category = "Tech",
        Language = "en",
        ModelVersion = "claude-3-haiku",
        ProcessedAt = DateTimeOffset.UtcNow,
        Tags = [],
        KeyFacts = [],
    };

    private static MediaFileEntity CreateMediaFileEntity(Guid articleId, string r2Key) => new()
    {
        Id = Guid.NewGuid(),
        ArticleId = articleId,
        R2Key = r2Key,
        OriginalUrl = "https://source.example.com/img.jpg",
        ContentType = "image/jpeg",
        SizeBytes = 51_200,
        Kind = "Image",
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
