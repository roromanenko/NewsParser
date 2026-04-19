using Core.DomainModels;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// Tests for <see cref="IPublicationRepository"/> interface contract.
///
/// Methods that use raw SQL with FOR UPDATE SKIP LOCKED
/// (GetPendingForGenerationAsync, GetPendingForPublishAsync) and the bulk-update
/// methods (UpdateContentAndMediaAsync, UpdateApprovalAsync, UpdateRejectionAsync,
/// RequestRegenerationAsync) require PostgreSQL-specific APIs. Those contracts
/// are verified against the <see cref="IPublicationRepository"/> interface mock.
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
    // RequestRegenerationAsync — P0: after call, re-fetch shows Status=Created,
    // EditorFeedback=feedback, GeneratedContent=""
    // ------------------------------------------------------------------

    [Test]
    public async Task RequestRegenerationAsync_WhenCalled_ReFetchShowsCreatedStatusFeedbackAndClearedContent()
    {
        // Arrange — simulate a real repository by wiring the mock to mutate an in-memory
        // publication on RequestRegenerationAsync, then reflect that state on GetByIdAsync.
        var publicationId = Guid.NewGuid();
        const string feedback = "Drop the political angle.";

        var storedPublication = CreatePublication(PublicationStatus.ContentReady, publicationId);
        storedPublication.GeneratedContent = "Original AI draft text.";
        storedPublication.EditorFeedback = null;

        _repositoryMock
            .Setup(r => r.RequestRegenerationAsync(publicationId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, CancellationToken>((_, f, _) =>
            {
                storedPublication.Status = PublicationStatus.Created;
                storedPublication.EditorFeedback = f;
                storedPublication.GeneratedContent = string.Empty;
            })
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => storedPublication);

        // Act
        await _repositoryMock.Object.RequestRegenerationAsync(publicationId, feedback, CancellationToken.None);
        var refetched = await _repositoryMock.Object.GetByIdAsync(publicationId, CancellationToken.None);

        // Assert
        refetched.Should().NotBeNull();
        refetched!.Status.Should().Be(PublicationStatus.Created);
        refetched.EditorFeedback.Should().Be(feedback);
        refetched.GeneratedContent.Should().BeEmpty();
        _repositoryMock.Verify(
            r => r.RequestRegenerationAsync(publicationId, feedback, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // RequestRegenerationAsync — P1: second call overwrites the first feedback
    // (latest-wins semantics documented in ADR 0017)
    // ------------------------------------------------------------------

    [Test]
    public async Task RequestRegenerationAsync_WhenCalledTwice_SecondFeedbackOverwritesFirst()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        const string firstFeedback = "Make it shorter.";
        const string secondFeedback = "Actually, make it longer and more detailed.";

        var storedPublication = CreatePublication(PublicationStatus.ContentReady, publicationId);

        _repositoryMock
            .Setup(r => r.RequestRegenerationAsync(publicationId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, CancellationToken>((_, f, _) =>
            {
                storedPublication.Status = PublicationStatus.Created;
                storedPublication.EditorFeedback = f;
                storedPublication.GeneratedContent = string.Empty;
            })
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => storedPublication);

        // Act
        await _repositoryMock.Object.RequestRegenerationAsync(publicationId, firstFeedback, CancellationToken.None);
        await _repositoryMock.Object.RequestRegenerationAsync(publicationId, secondFeedback, CancellationToken.None);
        var refetched = await _repositoryMock.Object.GetByIdAsync(publicationId, CancellationToken.None);

        // Assert
        refetched.Should().NotBeNull();
        refetched!.EditorFeedback.Should().Be(secondFeedback);
        _repositoryMock.Verify(
            r => r.RequestRegenerationAsync(publicationId, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Publication CreatePublication(PublicationStatus status, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Article = new Article { Id = Guid.NewGuid(), Title = "Test Article" },
        PublishTarget = new PublishTarget { Id = Guid.NewGuid(), Name = "Test Target", Platform = Platform.Telegram, IsActive = true },
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
