using Core.DomainModels;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Infrastructure.Services;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Services;

[TestFixture]
public class PublicationServiceTests
{
    private Mock<IEventRepository> _eventRepoMock = null!;
    private Mock<IPublicationRepository> _publicationRepoMock = null!;
    private Mock<IPublishTargetRepository> _publishTargetRepoMock = null!;
    private PublicationService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _eventRepoMock = new Mock<IEventRepository>();
        _publicationRepoMock = new Mock<IPublicationRepository>();
        _publishTargetRepoMock = new Mock<IPublishTargetRepository>();

        _sut = new PublicationService(
            _eventRepoMock.Object,
            _publicationRepoMock.Object,
            _publishTargetRepoMock.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PublicationService>.Instance);
    }

    // ------------------------------------------------------------------
    // CreateForEventAsync — P0: happy path creates and saves publication
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateForEventAsync_WhenEventIsActiveAndTargetIsActive_CreatesAndSavesPublication()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var editorId = Guid.NewGuid();
        var initiatorArticle = CreateArticle(role: ArticleRole.Initiator);

        var relatedEvent = CreateActiveEvent(eventId, [initiatorArticle]);
        var publishTarget = CreateActiveTarget(targetId);

        _eventRepoMock
            .Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relatedEvent);
        _publishTargetRepoMock
            .Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publishTarget);
        _publicationRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Publication>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.CreateForEventAsync(eventId, targetId, editorId);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(PublicationStatus.Created);
        result.EventId.Should().Be(eventId);
        result.PublishTargetId.Should().Be(targetId);
        result.Article.Id.Should().Be(initiatorArticle.Id);
        _publicationRepoMock.Verify(r => r.AddAsync(It.IsAny<Publication>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // CreateForEventAsync — P0: picks first article when no Initiator role
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateForEventAsync_WhenNoInitiatorArticle_UsesFirstArticle()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var article1 = CreateArticle(role: null);
        var article2 = CreateArticle(role: null);

        var relatedEvent = CreateActiveEvent(eventId, [article1, article2]);
        var publishTarget = CreateActiveTarget(targetId);

        _eventRepoMock
            .Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relatedEvent);
        _publishTargetRepoMock
            .Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publishTarget);
        _publicationRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Publication>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.CreateForEventAsync(eventId, targetId, Guid.NewGuid());

        // Assert
        result.Article.Id.Should().Be(article1.Id);
    }

    // ------------------------------------------------------------------
    // CreateForEventAsync — P1: throws KeyNotFoundException when event missing
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateForEventAsync_WhenEventNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var eventId = Guid.NewGuid();

        _eventRepoMock
            .Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Event?)null);

        // Act
        var act = async () => await _sut.CreateForEventAsync(eventId, Guid.NewGuid(), Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage($"*{eventId}*");
        _publicationRepoMock.Verify(r => r.AddAsync(It.IsAny<Publication>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // CreateForEventAsync — P1: throws InvalidOperationException when event not Active
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateForEventAsync_WhenEventIsNotActive_ThrowsInvalidOperationException()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var archivedEvent = CreateActiveEvent(eventId, []);
        archivedEvent.Status = EventStatus.Archived;

        _eventRepoMock
            .Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(archivedEvent);

        // Act
        var act = async () => await _sut.CreateForEventAsync(eventId, Guid.NewGuid(), Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{eventId}*");
    }

    // ------------------------------------------------------------------
    // CreateForEventAsync — P1: throws KeyNotFoundException when publish target missing
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateForEventAsync_WhenPublishTargetNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relatedEvent = CreateActiveEvent(eventId, [CreateArticle()]);

        _eventRepoMock
            .Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relatedEvent);
        _publishTargetRepoMock
            .Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublishTarget?)null);

        // Act
        var act = async () => await _sut.CreateForEventAsync(eventId, targetId, Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage($"*{targetId}*");
    }

    // ------------------------------------------------------------------
    // CreateForEventAsync — P1: throws InvalidOperationException when target is inactive
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateForEventAsync_WhenPublishTargetIsInactive_ThrowsInvalidOperationException()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relatedEvent = CreateActiveEvent(eventId, [CreateArticle()]);
        var inactiveTarget = CreateActiveTarget(targetId);
        inactiveTarget.IsActive = false;

        _eventRepoMock
            .Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relatedEvent);
        _publishTargetRepoMock
            .Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveTarget);

        // Act
        var act = async () => await _sut.CreateForEventAsync(eventId, targetId, Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{targetId}*");
    }

    // ------------------------------------------------------------------
    // CreateForEventAsync — P1: throws InvalidOperationException when event has no articles
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateForEventAsync_WhenEventHasNoArticles_ThrowsInvalidOperationException()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var emptyEvent = CreateActiveEvent(eventId, []);
        var publishTarget = CreateActiveTarget(targetId);

        _eventRepoMock
            .Setup(r => r.GetDetailAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyEvent);
        _publishTargetRepoMock
            .Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publishTarget);

        // Act
        var act = async () => await _sut.CreateForEventAsync(eventId, targetId, Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{eventId}*");
    }

    // ------------------------------------------------------------------
    // UpdateContentAsync — P0: updates content and media, returns updated publication
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateContentAsync_WhenPublicationIsContentReady_UpdatesAndReturnsPublication()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var publication = CreatePublication(publicationId, PublicationStatus.ContentReady);
        var newContent = "Updated post text.";
        var newMediaIds = new List<Guid> { Guid.NewGuid() };

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);
        _publicationRepoMock
            .Setup(r => r.UpdateContentAndMediaAsync(publicationId, newContent, newMediaIds, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.UpdateContentAsync(publicationId, newContent, newMediaIds);

        // Assert
        result.GeneratedContent.Should().Be(newContent);
        result.SelectedMediaFileIds.Should().BeEquivalentTo(newMediaIds);
        _publicationRepoMock.Verify(
            r => r.UpdateContentAndMediaAsync(publicationId, newContent, newMediaIds, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // UpdateContentAsync — P1: throws KeyNotFoundException when publication missing
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateContentAsync_WhenPublicationNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var publicationId = Guid.NewGuid();

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Publication?)null);

        // Act
        var act = async () => await _sut.UpdateContentAsync(publicationId, "text", []);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage($"*{publicationId}*");
    }

    // ------------------------------------------------------------------
    // UpdateContentAsync — P1: throws InvalidOperationException when status != ContentReady
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateContentAsync_WhenPublicationIsNotContentReady_ThrowsInvalidOperationException()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var publication = CreatePublication(publicationId, PublicationStatus.Approved);

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);

        // Act
        var act = async () => await _sut.UpdateContentAsync(publicationId, "text", []);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{publicationId}*");
    }

    // ------------------------------------------------------------------
    // ApproveAsync — P0: sets Approved status, records editor and approvedAt
    // ------------------------------------------------------------------

    [Test]
    public async Task ApproveAsync_WhenPublicationIsContentReady_SetsApprovedStatusAndReturnsPublication()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var editorId = Guid.NewGuid();
        var publication = CreatePublication(publicationId, PublicationStatus.ContentReady);

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);
        _publicationRepoMock
            .Setup(r => r.UpdateApprovalAsync(publicationId, editorId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ApproveAsync(publicationId, editorId);

        // Assert
        result.Status.Should().Be(PublicationStatus.Approved);
        result.ReviewedByEditorId.Should().Be(editorId);
        result.ApprovedAt.Should().NotBeNull();
        _publicationRepoMock.Verify(
            r => r.UpdateApprovalAsync(publicationId, editorId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // ApproveAsync — P1: throws KeyNotFoundException when publication missing
    // ------------------------------------------------------------------

    [Test]
    public async Task ApproveAsync_WhenPublicationNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var publicationId = Guid.NewGuid();

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Publication?)null);

        // Act
        var act = async () => await _sut.ApproveAsync(publicationId, Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage($"*{publicationId}*");
    }

    // ------------------------------------------------------------------
    // ApproveAsync — P1: throws InvalidOperationException when status != ContentReady
    // ------------------------------------------------------------------

    [Test]
    public async Task ApproveAsync_WhenPublicationIsAlreadyApproved_ThrowsInvalidOperationException()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var publication = CreatePublication(publicationId, PublicationStatus.Approved);

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);

        // Act
        var act = async () => await _sut.ApproveAsync(publicationId, Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{publicationId}*");
    }

    // ------------------------------------------------------------------
    // RejectAsync — P0: rejects a ContentReady publication
    // ------------------------------------------------------------------

    [Test]
    public async Task RejectAsync_WhenPublicationIsContentReady_SetsRejectedStatusWithReason()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var editorId = Guid.NewGuid();
        const string reason = "Content is inaccurate.";
        var publication = CreatePublication(publicationId, PublicationStatus.ContentReady);

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);
        _publicationRepoMock
            .Setup(r => r.UpdateRejectionAsync(publicationId, editorId, reason, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.RejectAsync(publicationId, editorId, reason);

        // Assert
        result.Status.Should().Be(PublicationStatus.Rejected);
        result.RejectionReason.Should().Be(reason);
        result.ReviewedByEditorId.Should().Be(editorId);
        result.RejectedAt.Should().NotBeNull();
        _publicationRepoMock.Verify(
            r => r.UpdateRejectionAsync(publicationId, editorId, reason, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // RejectAsync — P0: also accepts Approved status (rollback scenario)
    // ------------------------------------------------------------------

    [Test]
    public async Task RejectAsync_WhenPublicationIsApproved_SetsRejectedStatus()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var editorId = Guid.NewGuid();
        const string reason = "Approved in error.";
        var publication = CreatePublication(publicationId, PublicationStatus.Approved);

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);
        _publicationRepoMock
            .Setup(r => r.UpdateRejectionAsync(publicationId, editorId, reason, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.RejectAsync(publicationId, editorId, reason);

        // Assert
        result.Status.Should().Be(PublicationStatus.Rejected);
    }

    // ------------------------------------------------------------------
    // RejectAsync — P1: throws InvalidOperationException for Created status
    // ------------------------------------------------------------------

    [Test]
    public async Task RejectAsync_WhenPublicationIsCreated_ThrowsInvalidOperationException()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var publication = CreatePublication(publicationId, PublicationStatus.Created);

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);

        // Act
        var act = async () => await _sut.RejectAsync(publicationId, Guid.NewGuid(), "reason");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{publicationId}*");
    }

    // ------------------------------------------------------------------
    // RejectAsync — P1: throws KeyNotFoundException when publication missing
    // ------------------------------------------------------------------

    [Test]
    public async Task RejectAsync_WhenPublicationNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var publicationId = Guid.NewGuid();

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Publication?)null);

        // Act
        var act = async () => await _sut.RejectAsync(publicationId, Guid.NewGuid(), "reason");

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage($"*{publicationId}*");
    }

    // ------------------------------------------------------------------
    // SendAsync — P0: approves a ContentReady publication (fast-track send)
    // ------------------------------------------------------------------

    [Test]
    public async Task SendAsync_WhenPublicationIsContentReady_SetsApprovedStatusAndReturnsPublication()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var editorId = Guid.NewGuid();
        var publication = CreatePublication(publicationId, PublicationStatus.ContentReady);

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);
        _publicationRepoMock
            .Setup(r => r.UpdateApprovalAsync(publicationId, editorId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.SendAsync(publicationId, editorId);

        // Assert
        result.Status.Should().Be(PublicationStatus.Approved);
        result.ReviewedByEditorId.Should().Be(editorId);
        _publicationRepoMock.Verify(
            r => r.UpdateApprovalAsync(publicationId, editorId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // SendAsync — P0: also accepts already-Approved publications
    // ------------------------------------------------------------------

    [Test]
    public async Task SendAsync_WhenPublicationIsAlreadyApproved_ReturnsApprovedPublication()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var editorId = Guid.NewGuid();
        var publication = CreatePublication(publicationId, PublicationStatus.Approved);

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);
        _publicationRepoMock
            .Setup(r => r.UpdateApprovalAsync(publicationId, editorId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.SendAsync(publicationId, editorId);

        // Assert
        result.Status.Should().Be(PublicationStatus.Approved);
    }

    // ------------------------------------------------------------------
    // SendAsync — P1: throws InvalidOperationException for Created status
    // ------------------------------------------------------------------

    [Test]
    public async Task SendAsync_WhenPublicationIsCreated_ThrowsInvalidOperationException()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var publication = CreatePublication(publicationId, PublicationStatus.Created);

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);

        // Act
        var act = async () => await _sut.SendAsync(publicationId, Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{publicationId}*");
    }

    // ------------------------------------------------------------------
    // SendAsync — P1: throws KeyNotFoundException when publication missing
    // ------------------------------------------------------------------

    [Test]
    public async Task SendAsync_WhenPublicationNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var publicationId = Guid.NewGuid();

        _publicationRepoMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Publication?)null);

        // Act
        var act = async () => await _sut.SendAsync(publicationId, Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage($"*{publicationId}*");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Article CreateArticle(ArticleRole? role = ArticleRole.Initiator) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Article",
        Status = ArticleStatus.AnalysisDone,
        Role = role
    };

    private static Event CreateActiveEvent(Guid id, List<Article> articles) => new()
    {
        Id = id,
        Title = "Test Event",
        Summary = "Summary",
        Status = EventStatus.Active,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        Articles = articles
    };

    private static PublishTarget CreateActiveTarget(Guid id) => new()
    {
        Id = id,
        Name = "My Channel",
        Platform = Platform.Telegram,
        Identifier = "@mychannel",
        SystemPrompt = "Be concise.",
        IsActive = true
    };

    private static Publication CreatePublication(Guid id, PublicationStatus status) => new()
    {
        Id = id,
        Article = new Article { Id = Guid.NewGuid(), Title = "Test Article" },
        PublishTarget = new PublishTarget { Id = Guid.NewGuid(), Name = "My Channel", Platform = Platform.Telegram, IsActive = true },
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
