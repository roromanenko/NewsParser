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
/// pagination feature on <see cref="EventRepository"/>.
///
/// Covers the CountAsync bug-fix (previously filtered to Active only; now
/// counts all events regardless of status) and the sort direction applied
/// by GetPagedAsync.
/// </summary>
[TestFixture]
public class EventRepositorySearchSortTests
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
    // CountAsync — P0 (bug-fix): counts all events regardless of status
    // ------------------------------------------------------------------

    [Test]
    public async Task CountAsync_WhenEventsHaveMixedStatuses_CountsAllEvents()
    {
        // Arrange
        var active = CreateEventEntity("Active Event", EventStatus.Active, daysOld: 1);
        var archived = CreateEventEntity("Archived Event", EventStatus.Archived, daysOld: 2);

        _db.Events.AddRange(active, archived);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        var count = await _sut.CountAsync(search: null);

        // Assert
        count.Should().Be(2, "CountAsync must count all events, not just Active ones");
    }

    // ------------------------------------------------------------------
    // CountAsync — P0: null search with no events returns 0
    // ------------------------------------------------------------------

    [Test]
    public async Task CountAsync_WhenNoEventsExist_ReturnsZero()
    {
        // Act
        var count = await _sut.CountAsync(search: null);

        // Assert
        count.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // GetPagedAsync — P0: sortBy="oldest" orders by LastUpdatedAt ASC
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPagedAsync_WhenSortByIsOldest_ReturnsEventsOrderedByLastUpdatedAtAscending()
    {
        // Arrange
        var older = CreateEventEntity("Older Event", EventStatus.Active, daysOld: 5);
        var newer = CreateEventEntity("Newer Event", EventStatus.Active, daysOld: 1);

        _db.Events.AddRange(older, newer);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetPagedAsync(page: 1, pageSize: 10, search: null, sortBy: "oldest");

        // Assert
        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Older Event");
        result[1].Title.Should().Be("Newer Event");
    }

    // ------------------------------------------------------------------
    // GetPagedAsync — P0: sortBy="newest" orders by LastUpdatedAt DESC
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPagedAsync_WhenSortByIsNewest_ReturnsEventsOrderedByLastUpdatedAtDescending()
    {
        // Arrange
        var older = CreateEventEntity("Older Event", EventStatus.Active, daysOld: 5);
        var newer = CreateEventEntity("Newer Event", EventStatus.Active, daysOld: 1);

        _db.Events.AddRange(older, newer);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetPagedAsync(page: 1, pageSize: 10, search: null, sortBy: "newest");

        // Assert
        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Newer Event");
        result[1].Title.Should().Be("Older Event");
    }

    // ------------------------------------------------------------------
    // GetPagedAsync — P1: unknown sortBy falls back to DESC order
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPagedAsync_WhenSortByIsUnknownValue_FallsBackToDescendingOrder()
    {
        // Arrange
        var older = CreateEventEntity("Older Event", EventStatus.Active, daysOld: 5);
        var newer = CreateEventEntity("Newer Event", EventStatus.Active, daysOld: 1);

        _db.Events.AddRange(older, newer);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetPagedAsync(page: 1, pageSize: 10, search: null, sortBy: "random_value");

        // Assert
        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Newer Event", "unknown sortBy must fall back to newest-first (DESC)");
        result[1].Title.Should().Be("Older Event");
    }

    // ------------------------------------------------------------------
    // GetPagedAsync — P0: returns events of all statuses (no status filter)
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPagedAsync_WhenEventsHaveMixedStatuses_ReturnsAllEvents()
    {
        // Arrange
        var active = CreateEventEntity("Active Event", EventStatus.Active, daysOld: 1);
        var archived = CreateEventEntity("Archived Event", EventStatus.Archived, daysOld: 2);

        _db.Events.AddRange(active, archived);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetPagedAsync(page: 1, pageSize: 10, search: null, sortBy: "newest");

        // Assert
        result.Should().HaveCount(2);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static EventEntity CreateEventEntity(
        string title,
        EventStatus status,
        int daysOld) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Summary = "Summary",
        Status = status.ToString(),
        FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-daysOld),
        LastUpdatedAt = DateTimeOffset.UtcNow.AddDays(-daysOld),
        ArticleCount = 0,
    };
}
