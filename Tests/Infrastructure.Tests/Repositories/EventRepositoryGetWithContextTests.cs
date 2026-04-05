using FluentAssertions;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// Tests for EventRepository.GetWithContextAsync.
///
/// GetWithContextAsync loads an event with Articles (including their KeyFacts) and
/// EventUpdates, but intentionally does NOT include Contradictions.
///
/// The production NewsParserDbContext requires the pgvector extension and uses
/// HasColumnType("vector(768)") on article/event embeddings and an HNSW index,
/// which are PostgreSQL-specific and unsupported by EF InMemory. We use a
/// TestNewsParserDbContext subclass that overrides OnModelCreating to apply only
/// the portable relationship configuration, ignoring the pgvector-typed Embedding
/// properties so EF InMemory can construct the entity metadata graph.
///
/// After seeding each test calls _db.ChangeTracker.Clear() before querying so that
/// EF's relationship fixup (which would populate navigation properties from the
/// change tracker regardless of Include clauses) does not produce false positives.
/// </summary>
[TestFixture]
public class EventRepositoryGetWithContextTests
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
    // P0 — Returns null when the event id does not exist
    // ------------------------------------------------------------------

    [Test]
    public async Task GetWithContextAsync_WhenEventDoesNotExist_ReturnsNull()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _sut.GetWithContextAsync(nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P0 — Returns event with Articles (including KeyFacts) and EventUpdates populated
    // ------------------------------------------------------------------

    [Test]
    public async Task GetWithContextAsync_WhenEventExists_ReturnsEventWithArticlesAndEventUpdatesPopulated()
    {
        // Arrange
        var eventEntity = CreateEventEntity();

        var articleEntity = new ArticleEntity
        {
            Id = Guid.NewGuid(),
            Title = "Test Article",
            Status = "AnalysisDone",
            Sentiment = "Neutral",
            ProcessedAt = DateTimeOffset.UtcNow,
            KeyFacts = ["Fact one.", "Fact two."],
            EventId = eventEntity.Id,
        };

        var updateEntity = new EventUpdateEntity
        {
            Id = Guid.NewGuid(),
            EventId = eventEntity.Id,
            ArticleId = articleEntity.Id,
            FactSummary = "A significant new fact was reported.",
            IsPublished = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Events.Add(eventEntity);
        _db.Articles.Add(articleEntity);
        _db.EventUpdates.Add(updateEntity);
        await _db.SaveChangesAsync();

        // Clear the change tracker so the query relies solely on the Include
        // clauses rather than EF's relationship fixup from tracked entities.
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetWithContextAsync(eventEntity.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(eventEntity.Id);

        result.Articles.Should().HaveCount(1);
        result.Articles[0].Title.Should().Be("Test Article");
        result.Articles[0].KeyFacts.Should().BeEquivalentTo(["Fact one.", "Fact two."]);

        result.EventUpdates.Should().HaveCount(1);
        result.EventUpdates[0].FactSummary.Should().Be("A significant new fact was reported.");
    }

    // ------------------------------------------------------------------
    // P1 — Contradictions collection is empty even when contradictions exist
    //       in the database for that event (GetWithContextAsync does not Include them)
    // ------------------------------------------------------------------

    [Test]
    public async Task GetWithContextAsync_WhenContradictionsExistInDatabase_ReturnsEventWithEmptyContradictions()
    {
        // Arrange
        var eventEntity = CreateEventEntity();

        var articleEntity1 = new ArticleEntity
        {
            Id = Guid.NewGuid(),
            Title = "Article One",
            Status = "AnalysisDone",
            Sentiment = "Neutral",
            ProcessedAt = DateTimeOffset.UtcNow,
            EventId = eventEntity.Id,
        };

        var articleEntity2 = new ArticleEntity
        {
            Id = Guid.NewGuid(),
            Title = "Article Two",
            Status = "AnalysisDone",
            Sentiment = "Neutral",
            ProcessedAt = DateTimeOffset.UtcNow,
            EventId = eventEntity.Id,
        };

        var contradictionEntity = new ContradictionEntity
        {
            Id = Guid.NewGuid(),
            EventId = eventEntity.Id,
            Description = "Article One claims 5 casualties; Article Two claims 2.",
            IsResolved = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var contradictionArticle1 = new ContradictionArticleEntity
        {
            ContradictionId = contradictionEntity.Id,
            ArticleId = articleEntity1.Id,
        };

        var contradictionArticle2 = new ContradictionArticleEntity
        {
            ContradictionId = contradictionEntity.Id,
            ArticleId = articleEntity2.Id,
        };

        _db.Events.Add(eventEntity);
        _db.Articles.Add(articleEntity1);
        _db.Articles.Add(articleEntity2);
        _db.Contradictions.Add(contradictionEntity);
        _db.ContradictionArticles.Add(contradictionArticle1);
        _db.ContradictionArticles.Add(contradictionArticle2);
        await _db.SaveChangesAsync();

        // Clear the change tracker so EF's relationship fixup cannot populate
        // Contradictions from tracked entities — the Include clause is the sole source.
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetWithContextAsync(eventEntity.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Contradictions.Should().BeEmpty(
            "GetWithContextAsync intentionally omits Contradictions to keep the payload lightweight for AI classification");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static EventEntity CreateEventEntity() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Event",
        Summary = "Summary of the test event.",
        Status = "Active",
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        ArticleCount = 0,
    };
}

/// <summary>
/// A test-only DbContext subclass that replaces the production OnModelCreating
/// with a portable configuration compatible with the EF InMemory provider.
///
/// Relationship navigation properties are fully configured so that EF InMemory
/// can resolve Include() chains (Articles, EventUpdates, Contradictions).
/// The pgvector-typed Embedding properties on EventEntity and ArticleEntity are
/// explicitly ignored because the Pgvector.Vector type has no default constructor
/// and cannot be instantiated by the EF InMemory metadata resolver.
/// All other columns (KeyFacts as List&lt;string&gt;, Tags, etc.) are left to
/// EF convention discovery and work correctly with the InMemory provider.
/// </summary>
internal sealed class TestNewsParserDbContext : NewsParserDbContext
{
    public TestNewsParserDbContext(DbContextOptions<NewsParserDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Event — ignore pgvector Embedding; configure relationships
        modelBuilder.Entity<EventEntity>(b =>
        {
            b.HasKey(e => e.Id);
            b.Ignore(e => e.Embedding);
            b.HasMany(e => e.EventUpdates)
                .WithOne(eu => eu.Event)
                .HasForeignKey(eu => eu.EventId);
            b.HasMany(e => e.Contradictions)
                .WithOne(c => c.Event)
                .HasForeignKey(c => c.EventId);
            b.HasMany(e => e.Articles)
                .WithOne(a => a.Event)
                .HasForeignKey(a => a.EventId)
                .IsRequired(false);
        });

        // Article — ignore pgvector Embedding; configure relationships
        modelBuilder.Entity<ArticleEntity>(b =>
        {
            b.HasKey(a => a.Id);
            b.Ignore(a => a.Embedding);
            b.HasOne(a => a.Event)
                .WithMany(e => e.Articles)
                .HasForeignKey(a => a.EventId)
                .IsRequired(false);
        });

        // EventUpdate
        modelBuilder.Entity<EventUpdateEntity>(b =>
        {
            b.HasKey(eu => eu.Id);
            b.HasOne(eu => eu.Event)
                .WithMany(e => e.EventUpdates)
                .HasForeignKey(eu => eu.EventId);
            b.HasOne(eu => eu.Article)
                .WithMany()
                .HasForeignKey(eu => eu.ArticleId);
        });

        // Contradiction
        modelBuilder.Entity<ContradictionEntity>(b =>
        {
            b.HasKey(c => c.Id);
            b.HasOne(c => c.Event)
                .WithMany(e => e.Contradictions)
                .HasForeignKey(c => c.EventId);
            b.HasMany(c => c.ContradictionArticles)
                .WithOne(ca => ca.Contradiction)
                .HasForeignKey(ca => ca.ContradictionId);
        });

        // ContradictionArticle
        modelBuilder.Entity<ContradictionArticleEntity>(b =>
        {
            b.HasKey(ca => new { ca.ContradictionId, ca.ArticleId });
            b.HasOne(ca => ca.Contradiction)
                .WithMany(c => c.ContradictionArticles)
                .HasForeignKey(ca => ca.ContradictionId);
            b.HasOne(ca => ca.Article)
                .WithMany()
                .HasForeignKey(ca => ca.ArticleId);
        });
    }
}
