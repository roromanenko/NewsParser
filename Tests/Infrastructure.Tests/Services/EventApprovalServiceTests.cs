using Core.DomainModels;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Infrastructure.Services;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Services;

[TestFixture]
public class EventApprovalServiceTests
{
    private Mock<IEventRepository> _eventRepoMock = null!;
    private Mock<IPublicationRepository> _publicationRepoMock = null!;
    private Mock<IPublishTargetRepository> _publishTargetRepoMock = null!;
    private EventApprovalService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _eventRepoMock = new Mock<IEventRepository>();
        _publicationRepoMock = new Mock<IPublicationRepository>();
        _publishTargetRepoMock = new Mock<IPublishTargetRepository>();

        _sut = new EventApprovalService(
            _eventRepoMock.Object,
            _publicationRepoMock.Object,
            _publishTargetRepoMock.Object);
    }

    // ------------------------------------------------------------------
    // P0 — ApproveAsync creates one publication per publish target
    //       with correct EventId and ArticleId (initiator)
    // ------------------------------------------------------------------

    [Test]
    public async Task ApproveAsync_WhenEventHasInitiatorArticle_CreatesOnePublicationPerTarget()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var editorId = Guid.NewGuid();
        var targetId1 = Guid.NewGuid();
        var targetId2 = Guid.NewGuid();

        var initiatorArticle = CreateArticle(role: ArticleRole.Initiator);
        var relatedEvent = CreateEvent(eventId, [initiatorArticle]);

        var target1 = CreatePublishTarget(targetId1);
        var target2 = CreatePublishTarget(targetId2);

        _eventRepoMock.Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relatedEvent);
        _publishTargetRepoMock.Setup(r => r.GetByIdAsync(targetId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target1);
        _publishTargetRepoMock.Setup(r => r.GetByIdAsync(targetId2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target2);

        var capturedPublications = new List<Publication>();
        _publicationRepoMock
            .Setup(r => r.AddRangeAsync(
                initiatorArticle.Id,
                editorId,
                It.IsAny<List<Publication>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, List<Publication>, CancellationToken>(
                (_, _, pubs, _) => capturedPublications.AddRange(pubs))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ApproveAsync(
            eventId, editorId, [targetId1, targetId2], CancellationToken.None);

        // Assert
        capturedPublications.Should().HaveCount(2);
        capturedPublications.Should().AllSatisfy(p =>
        {
            p.EventId.Should().Be(eventId);
            p.Article.Id.Should().Be(initiatorArticle.Id);
            p.Status.Should().Be(PublicationStatus.Pending);
        });
        capturedPublications.Select(p => p.PublishTargetId)
            .Should().BeEquivalentTo(new[] { targetId1, targetId2 });
    }

    // ------------------------------------------------------------------
    // P0 — ApproveAsync sets event status to Approved
    // ------------------------------------------------------------------

    [Test]
    public async Task ApproveAsync_WhenCalled_SetsEventStatusToApproved()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relatedEvent = CreateEvent(eventId, [CreateArticle()]);

        SetupHappyPath(eventId, relatedEvent, [(targetId, CreatePublishTarget(targetId))]);

        // Act
        var result = await _sut.ApproveAsync(
            eventId, Guid.NewGuid(), [targetId], CancellationToken.None);

        // Assert
        _eventRepoMock.Verify(
            r => r.UpdateStatusAsync(eventId, EventStatus.Approved, It.IsAny<CancellationToken>()),
            Times.Once);
        result.Status.Should().Be(EventStatus.Approved);
    }

    // ------------------------------------------------------------------
    // P0 — ApproveAsync does NOT change individual article statuses
    //       (approval is now Event-level only)
    // ------------------------------------------------------------------

    [Test]
    public async Task ApproveAsync_WhenEventApproved_DoesNotUpdateIndividualArticleStatuses()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var article = CreateArticle(role: ArticleRole.Initiator);
        var relatedEvent = CreateEvent(eventId, [article]);

        SetupHappyPath(eventId, relatedEvent, [(targetId, CreatePublishTarget(targetId))]);

        var originalArticleStatus = article.Status;

        // Act
        var result = await _sut.ApproveAsync(
            eventId, Guid.NewGuid(), [targetId], CancellationToken.None);

        // Assert — articles in the returned event retain their original status
        result.Articles.Should().AllSatisfy(a =>
            a.Status.Should().Be(originalArticleStatus));
    }

    // ------------------------------------------------------------------
    // P1 — ApproveAsync throws KeyNotFoundException when event not found
    // ------------------------------------------------------------------

    [Test]
    public async Task ApproveAsync_WhenEventNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _eventRepoMock.Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Event?)null);

        // Act
        var act = async () => await _sut.ApproveAsync(
            eventId, Guid.NewGuid(), [Guid.NewGuid()], CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{eventId}*");
    }

    // ------------------------------------------------------------------
    // P1 — ApproveAsync throws InvalidOperationException when event has no articles
    // ------------------------------------------------------------------

    [Test]
    public async Task ApproveAsync_WhenEventHasNoArticles_ThrowsInvalidOperationException()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relatedEvent = CreateEvent(eventId, articles: []);

        _eventRepoMock.Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relatedEvent);
        _publishTargetRepoMock.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePublishTarget(targetId));

        // Act
        var act = async () => await _sut.ApproveAsync(
            eventId, Guid.NewGuid(), [targetId], CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{eventId}*");
    }

    // ------------------------------------------------------------------
    // P0 — RejectAsync sets event status to Rejected
    //       (article statuses are NOT touched — rejection is Event-level only)
    // ------------------------------------------------------------------

    [Test]
    public async Task RejectAsync_WhenEventExists_SetsEventStatusToRejected()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var article1 = CreateArticle();
        var article2 = CreateArticle();
        var relatedEvent = CreateEvent(eventId, [article1, article2]);

        _eventRepoMock.Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relatedEvent);

        // Act
        var result = await _sut.RejectAsync(eventId, Guid.NewGuid(), "Outdated", CancellationToken.None);

        // Assert
        _eventRepoMock.Verify(
            r => r.UpdateStatusAsync(eventId, EventStatus.Rejected, It.IsAny<CancellationToken>()),
            Times.Once);
        result.Status.Should().Be(EventStatus.Rejected);
    }

    // ------------------------------------------------------------------
    // P0 — RejectAsync does NOT change individual article statuses
    //       (rejection is now Event-level only)
    // ------------------------------------------------------------------

    [Test]
    public async Task RejectAsync_WhenEventRejected_DoesNotUpdateIndividualArticleStatuses()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var article1 = CreateArticle();
        var article2 = CreateArticle();
        var relatedEvent = CreateEvent(eventId, [article1, article2]);

        _eventRepoMock.Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relatedEvent);

        var originalStatus1 = article1.Status;
        var originalStatus2 = article2.Status;

        // Act
        var result = await _sut.RejectAsync(eventId, Guid.NewGuid(), "Outdated", CancellationToken.None);

        // Assert — articles in the returned event retain their original status
        result.Articles[0].Status.Should().Be(originalStatus1);
        result.Articles[1].Status.Should().Be(originalStatus2);
    }

    // ------------------------------------------------------------------
    // P1 — RejectAsync throws KeyNotFoundException when event not found
    // ------------------------------------------------------------------

    [Test]
    public async Task RejectAsync_WhenEventNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _eventRepoMock.Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Event?)null);

        // Act
        var act = async () => await _sut.RejectAsync(
            eventId, Guid.NewGuid(), "reason", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{eventId}*");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void SetupHappyPath(
        Guid eventId,
        Event relatedEvent,
        List<(Guid TargetId, PublishTarget Target)> targets)
    {
        _eventRepoMock.Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relatedEvent);

        foreach (var (targetId, target) in targets)
        {
            _publishTargetRepoMock
                .Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(target);
        }

        _publicationRepoMock
            .Setup(r => r.AddRangeAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<List<Publication>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static Article CreateArticle(ArticleRole role = ArticleRole.Initiator) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Article",
        Status = ArticleStatus.AnalysisDone,
        Role = role
    };

    private static Event CreateEvent(Guid id, List<Article> articles) => new()
    {
        Id = id,
        Title = "Test Event",
        Summary = "Summary",
        Status = EventStatus.Active,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        Articles = articles
    };

    private static PublishTarget CreatePublishTarget(Guid id) => new()
    {
        Id = id,
        Name = "Test Channel",
        Platform = Platform.Telegram,
        Identifier = "@test",
        IsActive = true
    };
}
