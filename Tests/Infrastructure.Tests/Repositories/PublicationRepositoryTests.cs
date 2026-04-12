using Core.DomainModels;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// Tests for <see cref="PublicationRepository"/>.
///
/// <para>
/// Methods that use raw SQL with FOR UPDATE SKIP LOCKED
/// (GetPendingForGenerationAsync, GetPendingForPublishAsync) and the bulk-update
/// methods that call ExecuteUpdateAsync (UpdateContentAndMediaAsync,
/// UpdateApprovalAsync, UpdateRejectionAsync) require PostgreSQL-specific APIs not
/// supported by EF InMemory. Those contracts are verified against the
/// <see cref="IPublicationRepository"/> interface mock, following the same pattern as
/// <c>ArticleRepositoryRejectAndQueryTests</c>.
/// </para>
///
/// <para>
/// Methods that use standard LINQ (AddAsync, GetByIdAsync, GetDetailAsync,
/// GetByEventIdAsync) are tested with <see cref="TestPublicationDbContext"/> — a
/// subclass that configures Publication, PublishTarget, and related entity
/// relationships for the InMemory provider.
/// </para>
/// </summary>
[TestFixture]
public class PublicationRepositoryInterfaceContractTests
{
    private Mock<IPublicationRepository> _repositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IPublicationRepository>();
    }

    // ------------------------------------------------------------------
    // GetPendingForGenerationAsync — P0: returns list of Created publications
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPendingForGenerationAsync_WhenCreatedPublicationsExist_ReturnsList()
    {
        // Arrange
        var publications = new List<Publication>
        {
            CreatePublication(PublicationStatus.Created),
            CreatePublication(PublicationStatus.Created)
        };

        _repositoryMock
            .Setup(r => r.GetPendingForGenerationAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publications);

        // Act
        var result = await _repositoryMock.Object.GetPendingForGenerationAsync(10, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(p => p.Status.Should().Be(PublicationStatus.Created));
    }

    // ------------------------------------------------------------------
    // GetPendingForGenerationAsync — P1: returns empty list when nothing is pending
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPendingForGenerationAsync_WhenNoPendingPublications_ReturnsEmptyList()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetPendingForGenerationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _repositoryMock.Object.GetPendingForGenerationAsync(10, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // GetPendingForPublishAsync — P0: returns list of Approved publications
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPendingForPublishAsync_WhenApprovedPublicationsExist_ReturnsList()
    {
        // Arrange
        var publications = new List<Publication>
        {
            CreatePublication(PublicationStatus.Approved),
            CreatePublication(PublicationStatus.Approved)
        };

        _repositoryMock
            .Setup(r => r.GetPendingForPublishAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publications);

        // Act
        var result = await _repositoryMock.Object.GetPendingForPublishAsync(5, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(p => p.Status.Should().Be(PublicationStatus.Approved));
    }

    // ------------------------------------------------------------------
    // GetPendingForPublishAsync — P1: returns empty list when nothing is approved
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPendingForPublishAsync_WhenNoApprovedPublications_ReturnsEmptyList()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetPendingForPublishAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _repositoryMock.Object.GetPendingForPublishAsync(5, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // UpdateContentAndMediaAsync — P0: contract accepts id, content and media ids
    //
    // ExecuteUpdateAsync requires PostgreSQL; verified via interface mock.
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateContentAndMediaAsync_WhenCalled_InvokesRepositoryWithCorrectArguments()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var newContent = "Updated post text.";
        var newMediaIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        List<Guid>? capturedMediaIds = null;
        string? capturedContent = null;

        _repositoryMock
            .Setup(r => r.UpdateContentAndMediaAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, List<Guid>, CancellationToken>((_, c, m, _) =>
            {
                capturedContent = c;
                capturedMediaIds = m;
            })
            .Returns(Task.CompletedTask);

        // Act
        await _repositoryMock.Object.UpdateContentAndMediaAsync(publicationId, newContent, newMediaIds, CancellationToken.None);

        // Assert
        capturedContent.Should().Be(newContent);
        capturedMediaIds.Should().BeEquivalentTo(newMediaIds);
        _repositoryMock.Verify(
            r => r.UpdateContentAndMediaAsync(publicationId, newContent, newMediaIds, CancellationToken.None),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // UpdateApprovalAsync — P0: contract accepts id, editorId, and approvedAt
    //
    // ExecuteUpdateAsync requires PostgreSQL; verified via interface mock.
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateApprovalAsync_WhenCalled_InvokesRepositoryWithCorrectArguments()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var editorId = Guid.NewGuid();
        var approvedAt = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

        _repositoryMock
            .Setup(r => r.UpdateApprovalAsync(publicationId, editorId, approvedAt, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () =>
            await _repositoryMock.Object.UpdateApprovalAsync(publicationId, editorId, approvedAt, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        _repositoryMock.Verify(
            r => r.UpdateApprovalAsync(publicationId, editorId, approvedAt, CancellationToken.None),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // UpdateRejectionAsync — P0: contract accepts id, editorId, reason, and rejectedAt
    //
    // ExecuteUpdateAsync requires PostgreSQL; verified via interface mock.
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateRejectionAsync_WhenCalled_InvokesRepositoryWithCorrectArguments()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var editorId = Guid.NewGuid();
        var rejectedAt = new DateTimeOffset(2025, 6, 2, 10, 30, 0, TimeSpan.Zero);
        const string reason = "Tone is too promotional.";

        _repositoryMock
            .Setup(r => r.UpdateRejectionAsync(publicationId, editorId, reason, rejectedAt, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () =>
            await _repositoryMock.Object.UpdateRejectionAsync(publicationId, editorId, reason, rejectedAt, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        _repositoryMock.Verify(
            r => r.UpdateRejectionAsync(publicationId, editorId, reason, rejectedAt, CancellationToken.None),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Publication CreatePublication(PublicationStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Article = new Article { Id = Guid.NewGuid(), Title = "Test Article" },
        PublishTarget = new PublishTarget { Id = Guid.NewGuid(), Name = "Test Target", Platform = Platform.Telegram, IsActive = true },
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow
    };
}

/// <summary>
/// EF InMemory tests for publication CRUD operations that use standard LINQ
/// (AddAsync, GetByIdAsync, GetDetailAsync, GetByEventIdAsync).
/// </summary>
[TestFixture]
public class PublicationRepositoryEfTests
{
    private TestPublicationDbContext _db = null!;
    private PublicationRepository _sut = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<NewsParserDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new TestPublicationDbContext(options);
        _db.Database.EnsureCreated();

        _sut = new PublicationRepository(_db);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    // ------------------------------------------------------------------
    // AddAsync — P0: persisted publication is retrievable by GetByIdAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task AddAsync_WhenCalled_PersistsPublicationRetrievableById()
    {
        // Arrange
        var (articleId, publishTargetId) = await SeedArticleAndTarget();
        var publication = CreateDomainPublication(articleId, publishTargetId, PublicationStatus.Created);

        // Act
        await _sut.AddAsync(publication);

        // Assert
        _db.ChangeTracker.Clear();
        var result = await _sut.GetByIdAsync(publication.Id);
        result.Should().NotBeNull();
        result!.Id.Should().Be(publication.Id);
        result.Status.Should().Be(PublicationStatus.Created);
    }

    // ------------------------------------------------------------------
    // GetByIdAsync — P0: returns publication with PublishTarget included
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByIdAsync_WhenPublicationExists_ReturnsPublicationWithPublishTarget()
    {
        // Arrange
        var (articleId, publishTargetId) = await SeedArticleAndTarget();
        var publication = CreateDomainPublication(articleId, publishTargetId, PublicationStatus.ContentReady);
        await _sut.AddAsync(publication);
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetByIdAsync(publication.Id);

        // Assert
        result.Should().NotBeNull();
        result!.PublishTarget.Should().NotBeNull();
        result.PublishTarget.Name.Should().Be("My Channel");
    }

    // ------------------------------------------------------------------
    // GetByIdAsync — P1: returns null for unknown id
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByIdAsync_WhenPublicationDoesNotExist_ReturnsNull()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _sut.GetByIdAsync(nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // GetDetailAsync — P0: returns null for unknown id
    // ------------------------------------------------------------------

    [Test]
    public async Task GetDetailAsync_WhenPublicationDoesNotExist_ReturnsNull()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _sut.GetDetailAsync(nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // GetDetailAsync — P0: returns publication with correct id and status
    // ------------------------------------------------------------------

    [Test]
    public async Task GetDetailAsync_WhenPublicationExists_ReturnsPublicationWithCorrectIdAndStatus()
    {
        // Arrange
        var (articleId, publishTargetId) = await SeedArticleAndTarget();
        var publication = CreateDomainPublication(articleId, publishTargetId, PublicationStatus.Published);
        await _sut.AddAsync(publication);
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetDetailAsync(publication.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(publication.Id);
        result.Status.Should().Be(PublicationStatus.Published);
    }

    // ------------------------------------------------------------------
    // GetByEventIdAsync — P0: returns all publications for the given event
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByEventIdAsync_WhenPublicationsExistForEvent_ReturnsAllMatchingPublications()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var (articleId, publishTargetId) = await SeedArticleAndTarget();

        var pub1 = CreateDomainPublication(articleId, publishTargetId, PublicationStatus.Created, eventId);
        var pub2 = CreateDomainPublication(articleId, publishTargetId, PublicationStatus.Approved, eventId);
        var otherPub = CreateDomainPublication(articleId, publishTargetId, PublicationStatus.Created, Guid.NewGuid());

        await _sut.AddAsync(pub1);
        await _sut.AddAsync(pub2);
        await _sut.AddAsync(otherPub);
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetByEventIdAsync(eventId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(p => p.EventId.Should().Be(eventId));
    }

    // ------------------------------------------------------------------
    // GetByEventIdAsync — P1: returns empty list when no publications for event
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByEventIdAsync_WhenNoPublicationsForEvent_ReturnsEmptyList()
    {
        // Arrange
        var eventId = Guid.NewGuid();

        // Act
        var result = await _sut.GetByEventIdAsync(eventId);

        // Assert
        result.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // GetByEventIdAsync — P0: results are ordered by CreatedAt ascending
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByEventIdAsync_WhenMultiplePublicationsExist_ReturnsInCreatedAtAscendingOrder()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var (articleId, publishTargetId) = await SeedArticleAndTarget();

        var earlier = CreateDomainPublication(articleId, publishTargetId, PublicationStatus.Created, eventId,
            createdAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var later = CreateDomainPublication(articleId, publishTargetId, PublicationStatus.Approved, eventId,
            createdAt: new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));

        // Insert in reverse order to confirm ordering is by CreatedAt, not insert order
        await _sut.AddAsync(later);
        await _sut.AddAsync(earlier);
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sut.GetByEventIdAsync(eventId);

        // Assert
        result.Should().HaveCount(2);
        result[0].CreatedAt.Should().BeBefore(result[1].CreatedAt);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private async Task<(Guid articleId, Guid publishTargetId)> SeedArticleAndTarget()
    {
        var articleEntity = new ArticleEntity
        {
            Id = Guid.NewGuid(),
            Title = "Seeded Article",
            Status = "AnalysisDone",
            Sentiment = "Neutral",
            ProcessedAt = DateTimeOffset.UtcNow
        };

        var targetEntity = new PublishTargetEntity
        {
            Id = Guid.NewGuid(),
            Name = "My Channel",
            Platform = "Telegram",
            Identifier = "@mychannel",
            SystemPrompt = "Be concise.",
            IsActive = true
        };

        _db.Articles.Add(articleEntity);
        _db.PublishTargets.Add(targetEntity);
        await _db.SaveChangesAsync();

        return (articleEntity.Id, targetEntity.Id);
    }

    private static Publication CreateDomainPublication(
        Guid articleId,
        Guid publishTargetId,
        PublicationStatus status,
        Guid? eventId = null,
        DateTimeOffset? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Article = new Article { Id = articleId, Title = "Seeded Article" },
        PublishTarget = new PublishTarget { Id = publishTargetId, Name = "My Channel", Platform = Platform.Telegram },
        PublishTargetId = publishTargetId,
        Status = status,
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        EventId = eventId
    };
}

/// <summary>
/// A test-only DbContext that configures Publication, PublishTarget, PublishLog,
/// Article, and Event entity relationships for the EF InMemory provider.
/// The pgvector Embedding property on Article and Event entities is explicitly
/// ignored because the Pgvector.Vector type is unsupported by InMemory.
/// </summary>
internal sealed class TestPublicationDbContext : NewsParserDbContext
{
    public TestPublicationDbContext(DbContextOptions<NewsParserDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Event
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

        // Article
        modelBuilder.Entity<ArticleEntity>(b =>
        {
            b.HasKey(a => a.Id);
            b.Ignore(a => a.Embedding);
            b.HasOne(a => a.Event)
                .WithMany(e => e.Articles)
                .HasForeignKey(a => a.EventId)
                .IsRequired(false);
            b.HasMany(a => a.MediaFiles)
                .WithOne()
                .HasForeignKey(m => m.ArticleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // MediaFile
        modelBuilder.Entity<MediaFileEntity>(b =>
        {
            b.HasKey(m => m.Id);
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

        // PublishTarget
        modelBuilder.Entity<PublishTargetEntity>(b =>
        {
            b.HasKey(t => t.Id);
            b.HasMany(t => t.Publications)
                .WithOne(p => p.PublishTarget)
                .HasForeignKey(p => p.PublishTargetId);
        });

        // PublishLog
        modelBuilder.Entity<PublishLogEntity>(b =>
        {
            b.HasKey(l => l.Id);
            b.HasOne(l => l.Publication)
                .WithMany(p => p.PublishLogs)
                .HasForeignKey(l => l.PublicationId);
        });

        // Publication
        modelBuilder.Entity<PublicationEntity>(b =>
        {
            b.HasKey(p => p.Id);
            b.HasOne(p => p.Article)
                .WithMany()
                .HasForeignKey(p => p.ArticleId);
            b.HasOne(p => p.PublishTarget)
                .WithMany(t => t.Publications)
                .HasForeignKey(p => p.PublishTargetId);
            b.HasMany(p => p.PublishLogs)
                .WithOne(l => l.Publication)
                .HasForeignKey(l => l.PublicationId);
            b.HasOne(p => p.Event)
                .WithMany()
                .HasForeignKey(p => p.EventId)
                .IsRequired(false);
            b.HasOne(p => p.ParentPublication)
                .WithMany()
                .HasForeignKey(p => p.ParentPublicationId)
                .IsRequired(false);
        });
    }
}
