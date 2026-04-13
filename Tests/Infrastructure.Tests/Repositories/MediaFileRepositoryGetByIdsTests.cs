using Core.DomainModels;
using FluentAssertions;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// EF Core InMemory tests for <see cref="MediaFileRepository.GetByIdsAsync"/>.
/// Uses <see cref="TestNewsParserDbContext"/> (defined in EventRepositoryGetWithContextTests.cs)
/// to keep the InMemory provider compatible by ignoring pgvector columns.
/// </summary>
[TestFixture]
public class MediaFileRepositoryGetByIdsTests
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
    // P0 — Only the requested IDs are returned when multiple records exist
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByIdsAsync_WhenMultipleRecordsExist_ReturnsOnlyRequestedIds()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var fileA = CreateMediaFile(articleId, "https://cdn.example.com/a.jpg");
        var fileB = CreateMediaFile(articleId, "https://cdn.example.com/b.jpg");
        var fileC = CreateMediaFile(articleId, "https://cdn.example.com/c.jpg");

        await _sut.AddAsync(fileA);
        await _sut.AddAsync(fileB);
        await _sut.AddAsync(fileC);

        // Act
        var result = await _sut.GetByIdsAsync([fileA.Id, fileC.Id]);

        // Assert
        result.Should().HaveCount(2);
        result.Select(r => r.Id).Should().BeEquivalentTo(new[] { fileA.Id, fileC.Id });
    }

    // ------------------------------------------------------------------
    // P1 — No IDs match → empty list returned
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByIdsAsync_WhenNoIdsMatch_ReturnsEmptyList()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        await _sut.AddAsync(CreateMediaFile(articleId, "https://cdn.example.com/a.jpg"));

        var nonExistentIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        // Act
        var result = await _sut.GetByIdsAsync(nonExistentIds);

        // Assert
        result.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // P2 — All IDs match → all items returned
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByIdsAsync_WhenAllIdsMatch_ReturnsAllItems()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var fileA = CreateMediaFile(articleId, "https://cdn.example.com/a.jpg");
        var fileB = CreateMediaFile(articleId, "https://cdn.example.com/b.jpg");

        await _sut.AddAsync(fileA);
        await _sut.AddAsync(fileB);

        // Act
        var result = await _sut.GetByIdsAsync([fileA.Id, fileB.Id]);

        // Assert
        result.Should().HaveCount(2);
        result.Select(r => r.Id).Should().BeEquivalentTo(new[] { fileA.Id, fileB.Id });
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
        SizeBytes = 512_000,
        Kind = MediaKind.Image,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
