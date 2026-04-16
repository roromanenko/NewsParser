using Core.DomainModels;
using Core.Interfaces.AI;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Infrastructure.Persistence.UnitOfWork;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Services;

[TestFixture]
public class EventServiceMergeAsyncTests
{
    private Mock<IEventRepository> _eventRepoMock = null!;
    private Mock<IGeminiEmbeddingService> _embeddingServiceMock = null!;
    private Mock<IEventSummaryUpdater> _summaryUpdaterMock = null!;
    private Mock<IEventTitleGenerator> _titleGeneratorMock = null!;
    private Mock<IUnitOfWork> _uowMock = null!;
    private EventService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _eventRepoMock = new Mock<IEventRepository>();
        _embeddingServiceMock = new Mock<IGeminiEmbeddingService>();
        _summaryUpdaterMock = new Mock<IEventSummaryUpdater>();
        _titleGeneratorMock = new Mock<IEventTitleGenerator>();
        _uowMock = new Mock<IUnitOfWork>();

        _sut = new EventService(
            _eventRepoMock.Object,
            _embeddingServiceMock.Object,
            _summaryUpdaterMock.Object,
            _titleGeneratorMock.Object,
            _uowMock.Object,
            NullLogger<EventService>.Instance);
    }

    // ------------------------------------------------------------------
    // MergeAsync — P0: happy path calls Begin before repository merge
    // ------------------------------------------------------------------

    [Test]
    public async Task MergeAsync_WhenBothEventsExist_CallsBeginBeforeRepositoryMerge()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var callOrder = new List<string>();

        SetupActiveEvents(sourceId, targetId);
        _uowMock.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => callOrder.Add("begin"));
        _eventRepoMock.Setup(r => r.MergeAsync(sourceId, targetId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => callOrder.Add("merge"));
        _uowMock.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => callOrder.Add("commit"));
        SetupAiEnrichmentDefaults();

        // Act
        await _sut.MergeAsync(sourceId, targetId, CancellationToken.None);

        // Assert
        callOrder.Should().StartWith(["begin", "merge", "commit"]);
    }

    // ------------------------------------------------------------------
    // MergeAsync — P0: CommitAsync is called after a successful repository merge
    // ------------------------------------------------------------------

    [Test]
    public async Task MergeAsync_WhenRepositoryMergeSucceeds_CallsCommitAsync()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        SetupActiveEvents(sourceId, targetId);
        _uowMock.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _eventRepoMock.Setup(r => r.MergeAsync(sourceId, targetId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        SetupAiEnrichmentDefaults();

        // Act
        await _sut.MergeAsync(sourceId, targetId, CancellationToken.None);

        // Assert
        _uowMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uowMock.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // MergeAsync — P1: repository failure triggers RollbackAsync and rethrows
    // ------------------------------------------------------------------

    [Test]
    public async Task MergeAsync_WhenRepositoryMergeThrows_CallsRollbackAndRethrows()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var dbException = new InvalidOperationException("DB error");

        SetupActiveEvents(sourceId, targetId);
        _uowMock.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _eventRepoMock.Setup(r => r.MergeAsync(sourceId, targetId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(dbException);
        _uowMock.Setup(u => u.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        var act = async () => await _sut.MergeAsync(sourceId, targetId, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB error");
        _uowMock.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // MergeAsync — P1: archived source event throws before any DB interaction
    // ------------------------------------------------------------------

    [Test]
    public async Task MergeAsync_WhenSourceEventIsArchived_ThrowsWithoutBeginningTransaction()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _eventRepoMock.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEvent(sourceId, EventStatus.Archived));
        _eventRepoMock.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEvent(targetId, EventStatus.Active));

        // Act
        var act = async () => await _sut.MergeAsync(sourceId, targetId, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{sourceId}*");
        _uowMock.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // MergeAsync — P1: source event not found throws KeyNotFoundException
    // ------------------------------------------------------------------

    [Test]
    public async Task MergeAsync_WhenSourceEventNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _eventRepoMock.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Event?)null);

        // Act
        var act = async () => await _sut.MergeAsync(sourceId, targetId, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage($"*{sourceId}*");
        _uowMock.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // MergeAsync — P0: AI enrichment runs AFTER CommitAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task MergeAsync_WhenMergeSucceeds_CallsAiEnrichmentAfterCommit()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var callOrder = new List<string>();

        SetupActiveEvents(sourceId, targetId);
        _uowMock.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _eventRepoMock.Setup(r => r.MergeAsync(sourceId, targetId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => callOrder.Add("commit"));
        _summaryUpdaterMock.Setup(s => s.UpdateSummaryAsync(It.IsAny<Event>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("updated summary")
            .Callback(() => callOrder.Add("ai_summary"));
        _titleGeneratorMock.Setup(t => t.GenerateTitleAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("generated title");
        _embeddingServiceMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f });
        _eventRepoMock.Setup(r => r.UpdateSummaryTitleAndEmbeddingAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.MergeAsync(sourceId, targetId, CancellationToken.None);

        // Assert
        var commitIndex = callOrder.IndexOf("commit");
        var aiIndex = callOrder.IndexOf("ai_summary");
        commitIndex.Should().BeGreaterThanOrEqualTo(0);
        aiIndex.Should().BeGreaterThan(commitIndex, "AI enrichment must run only after the transaction is committed");
    }

    // ------------------------------------------------------------------
    // MergeAsync — P1: AI enrichment failure does not rethrow after successful commit
    // ------------------------------------------------------------------

    [Test]
    public async Task MergeAsync_WhenAiEnrichmentFails_DoesNotRethrowAndMergeIsPreserved()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        SetupActiveEvents(sourceId, targetId);
        _uowMock.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _eventRepoMock.Setup(r => r.MergeAsync(sourceId, targetId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _summaryUpdaterMock.Setup(s => s.UpdateSummaryAsync(It.IsAny<Event>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("AI timeout"));

        // Act
        var act = async () => await _sut.MergeAsync(sourceId, targetId, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync("AI enrichment failure must be swallowed; the merge itself already committed");
        _uowMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void SetupActiveEvents(Guid sourceId, Guid targetId)
    {
        _eventRepoMock.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEvent(sourceId, EventStatus.Active, summary: "source summary", title: "Source Title"));
        _eventRepoMock.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEvent(targetId, EventStatus.Active, summary: "target summary", title: "Target Title"));
    }

    private void SetupAiEnrichmentDefaults()
    {
        _summaryUpdaterMock.Setup(s => s.UpdateSummaryAsync(It.IsAny<Event>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("merged summary");
        _titleGeneratorMock.Setup(t => t.GenerateTitleAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("merged title");
        _embeddingServiceMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f });
        _eventRepoMock.Setup(r => r.UpdateSummaryTitleAndEmbeddingAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static Event CreateEvent(Guid id, EventStatus status, string summary = "summary", string title = "title") => new()
    {
        Id = id,
        Title = title,
        Summary = summary,
        Status = status,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow
    };
}

[TestFixture]
public class EventServiceReclassifyArticleAsyncTests
{
    private Mock<IEventRepository> _eventRepoMock = null!;
    private Mock<IGeminiEmbeddingService> _embeddingServiceMock = null!;
    private Mock<IEventSummaryUpdater> _summaryUpdaterMock = null!;
    private Mock<IEventTitleGenerator> _titleGeneratorMock = null!;
    private Mock<IUnitOfWork> _uowMock = null!;
    private EventService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _eventRepoMock = new Mock<IEventRepository>();
        _embeddingServiceMock = new Mock<IGeminiEmbeddingService>();
        _summaryUpdaterMock = new Mock<IEventSummaryUpdater>();
        _titleGeneratorMock = new Mock<IEventTitleGenerator>();
        _uowMock = new Mock<IUnitOfWork>();

        _sut = new EventService(
            _eventRepoMock.Object,
            _embeddingServiceMock.Object,
            _summaryUpdaterMock.Object,
            _titleGeneratorMock.Object,
            _uowMock.Object,
            NullLogger<EventService>.Instance);
    }

    // ------------------------------------------------------------------
    // ReclassifyArticleAsync — P0: assigns article and marks reclassified
    // ------------------------------------------------------------------

    [Test]
    public async Task ReclassifyArticleAsync_WhenEventAndArticleExist_CallsAssignAndMarkReclassified()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var targetEventId = Guid.NewGuid();
        var articleId = Guid.NewGuid();
        var article = CreateArticle(articleId, ArticleRole.Update);
        var evt = CreateEventWithArticles(eventId, [article]);

        _eventRepoMock.Setup(r => r.GetByIdAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evt);
        _eventRepoMock.Setup(r => r.AssignArticleToEventAsync(articleId, targetEventId, ArticleRole.Initiator, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _eventRepoMock.Setup(r => r.MarkAsReclassifiedAsync(articleId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ReclassifyArticleAsync(eventId, articleId, targetEventId, ArticleRole.Initiator, CancellationToken.None);

        // Assert
        _eventRepoMock.Verify(r => r.AssignArticleToEventAsync(articleId, targetEventId, ArticleRole.Initiator, It.IsAny<CancellationToken>()), Times.Once);
        _eventRepoMock.Verify(r => r.MarkAsReclassifiedAsync(articleId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // ReclassifyArticleAsync — P1: event not found throws KeyNotFoundException
    // ------------------------------------------------------------------

    [Test]
    public async Task ReclassifyArticleAsync_WhenEventNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var eventId = Guid.NewGuid();

        _eventRepoMock.Setup(r => r.GetByIdAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Event?)null);

        // Act
        var act = async () => await _sut.ReclassifyArticleAsync(
            eventId, Guid.NewGuid(), Guid.NewGuid(), ArticleRole.Update, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage($"*{eventId}*");
        _eventRepoMock.Verify(r => r.AssignArticleToEventAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ArticleRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // ReclassifyArticleAsync — P1: article not in event throws KeyNotFoundException
    // ------------------------------------------------------------------

    [Test]
    public async Task ReclassifyArticleAsync_WhenArticleNotInEvent_ThrowsKeyNotFoundException()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var articleId = Guid.NewGuid();
        var evt = CreateEventWithArticles(eventId, []); // empty — article is not present

        _eventRepoMock.Setup(r => r.GetByIdAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evt);

        // Act
        var act = async () => await _sut.ReclassifyArticleAsync(
            eventId, articleId, Guid.NewGuid(), ArticleRole.Update, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage($"*{articleId}*");
        _eventRepoMock.Verify(r => r.AssignArticleToEventAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ArticleRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // ReclassifyArticleAsync — P2: same event and same role is a no-op
    // ------------------------------------------------------------------

    [Test]
    public async Task ReclassifyArticleAsync_WhenSameEventAndSameRole_ReturnsWithoutCallingRepository()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var articleId = Guid.NewGuid();
        var article = CreateArticle(articleId, ArticleRole.Update);
        var evt = CreateEventWithArticles(eventId, [article]);

        _eventRepoMock.Setup(r => r.GetByIdAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evt);

        // Act
        await _sut.ReclassifyArticleAsync(
            eventId, articleId, eventId, ArticleRole.Update, CancellationToken.None);

        // Assert
        _eventRepoMock.Verify(r => r.AssignArticleToEventAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ArticleRole>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventRepoMock.Verify(r => r.MarkAsReclassifiedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Article CreateArticle(Guid id, ArticleRole role) => new()
    {
        Id = id,
        Title = "Test Article",
        Role = role
    };

    private static Event CreateEventWithArticles(Guid id, List<Article> articles) => new()
    {
        Id = id,
        Title = "Test Event",
        Summary = "Summary",
        Status = EventStatus.Active,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        Articles = articles
    };
}
