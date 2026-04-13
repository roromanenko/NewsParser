using Core.DomainModels;
using FluentAssertions;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// EF Core InMemory tests verifying that <see cref="EventRepository.GetDetailAsync"/>
/// eagerly loads <c>MediaFiles</c> on every nested article via <c>ThenInclude</c>, and
/// that <see cref="EventRepository.GetPagedAsync"/> does NOT load media rows on articles
/// (regression guard — media must not appear in paged list queries).
///
/// Uses <see cref="TestNewsParserDbContext"/> (defined in
/// <c>EventRepositoryGetWithContextTests.cs</c>) which strips pgvector columns and
/// wires up all navigation properties for EF InMemory.
/// </summary>
[TestFixture]
public class EventRepositoryGetDetailWithMediaTests
{
    private TestNewsParserDbContext _db = null!;
    private EventRepository _sut = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<NewsParserDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new TestNewsParserDbContext(options);
        _sut = new EventRepository(_db);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    // ------------------------------------------------------------------
    // GetDetailAsync — P0: MediaFiles are populated on each nested article
    // ------------------------------------------------------------------

    [Test]
    public async Task GetDetailAsync_WhenEachArticleHasOneMediaFile_PopulatesMediaFilesOnBothArticles()
    {
        // Arrange
        var eventEntity = CreateEventEntity();

        var article1 = CreateArticleEntity(eventEntity.Id);
        var article2 = CreateArticleEntity(eventEntity.Id);

        var media1 = CreateMediaFileEntity(article1.Id, r2Key: "a1/photo.jpg");
        var media2 = CreateMediaFileEntity(article2.Id, r2Key: "a2/video.mp4");

        _db.Events.Add(eventEntity);
        _db.Articles.Add(article1);
        _db.Articles.Add(article2);
        _db.MediaFiles.Add(media1);
        _db.MediaFiles.Add(media2);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetDetailAsync(eventEntity.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Articles.Should().HaveCount(2);

        var domainArticle1 = result.Articles.Single(a => a.Id == article1.Id);
        domainArticle1.MediaFiles.Should().HaveCount(1);
        domainArticle1.MediaFiles[0].R2Key.Should().Be("a1/photo.jpg");

        var domainArticle2 = result.Articles.Single(a => a.Id == article2.Id);
        domainArticle2.MediaFiles.Should().HaveCount(1);
        domainArticle2.MediaFiles[0].R2Key.Should().Be("a2/video.mp4");
    }

    // ------------------------------------------------------------------
    // GetDetailAsync — P1: article with no media returns empty MediaFiles
    // ------------------------------------------------------------------

    [Test]
    public async Task GetDetailAsync_WhenArticleHasNoMediaFiles_ReturnsArticleWithEmptyMediaFiles()
    {
        // Arrange
        var eventEntity = CreateEventEntity();
        var articleEntity = CreateArticleEntity(eventEntity.Id);

        _db.Events.Add(eventEntity);
        _db.Articles.Add(articleEntity);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetDetailAsync(eventEntity.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Articles.Should().HaveCount(1);
        result.Articles[0].MediaFiles.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // GetPagedAsync — P0 (regression guard): does NOT populate MediaFiles
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPagedAsync_WhenArticlesHaveMediaFiles_DoesNotPopulateMediaFilesOnArticles()
    {
        // Arrange
        var eventEntity = CreateEventEntity();
        var articleEntity = CreateArticleEntity(eventEntity.Id);
        var media = CreateMediaFileEntity(articleEntity.Id, r2Key: "paged/img.jpg");

        _db.Events.Add(eventEntity);
        _db.Articles.Add(articleEntity);
        _db.MediaFiles.Add(media);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();

        // Act
        var results = await _sut.GetPagedAsync(page: 1, pageSize: 10, search: null, sortBy: "newest");

        // Assert
        results.Should().HaveCount(1);
        results[0].Articles.Should().HaveCount(1);
        results[0].Articles[0].MediaFiles.Should().BeEmpty(
            "GetPagedAsync does not ThenInclude MediaFiles — media must not leak into list queries");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static EventEntity CreateEventEntity() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Event",
        Summary = "Summary of the event.",
        Status = "Active",
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        ArticleCount = 0,
    };

    private static ArticleEntity CreateArticleEntity(Guid eventId) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Article",
        Status = "AnalysisDone",
        Sentiment = "Neutral",
        Category = "Tech",
        Language = "en",
        ModelVersion = "claude-3-haiku",
        ProcessedAt = DateTimeOffset.UtcNow,
        EventId = eventId,
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
