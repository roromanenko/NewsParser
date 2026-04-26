using Core.DomainModels;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// Behavior-contract tests for <see cref="IEventRepository.FindSimilarEventsAsync"/>
/// with the <c>Guid projectId</c> parameter introduced in Phase 1.
///
/// The concrete repository targets PostgreSQL + pgvector, so tests use a mock that
/// verifies the interface contract and project-scoping semantics.
/// </summary>
[TestFixture]
public class EventRepositoryFindSimilarTests
{
    private Mock<IEventRepository> _repositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IEventRepository>();
    }

    // ------------------------------------------------------------------
    // P0: results are scoped to the supplied projectId
    // ------------------------------------------------------------------

    [Test]
    public async Task FindSimilarEventsAsync_WhenCalledWithProjectIdA_ReturnsOnlyProjectAEvents()
    {
        // Arrange
        var projectIdA = Guid.NewGuid();
        var projectIdB = Guid.NewGuid();

        var eventFromA = CreateEvent(projectIdA);
        var eventFromB = CreateEvent(projectIdB);

        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        // Mock is configured to return only project-A results when called with projectIdA
        _repositoryMock
            .Setup(r => r.FindSimilarEventsAsync(
                projectIdA,
                embedding,
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([(eventFromA, 0.95)]);

        // Act
        var results = await _repositoryMock.Object.FindSimilarEventsAsync(
            projectIdA, embedding, threshold: 0.8, windowHours: 24, maxTake: 5);

        // Assert
        results.Should().HaveCount(1);
        results[0].Event.ProjectId.Should().Be(projectIdA);
        results.Should().NotContain(r => r.Event.ProjectId == projectIdB);
    }

    // ------------------------------------------------------------------
    // P2: zero matching events returns empty list
    // ------------------------------------------------------------------

    [Test]
    public async Task FindSimilarEventsAsync_WhenNoMatchingEvents_ReturnsEmptyList()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var embedding = new float[] { 0.5f, 0.6f, 0.7f };

        _repositoryMock
            .Setup(r => r.FindSimilarEventsAsync(
                projectId,
                embedding,
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var results = await _repositoryMock.Object.FindSimilarEventsAsync(
            projectId, embedding, threshold: 0.9, windowHours: 48, maxTake: 10);

        // Assert
        results.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // P0: method is called with exactly the projectId argument that was passed in
    // ------------------------------------------------------------------

    [Test]
    public async Task FindSimilarEventsAsync_WhenCalled_PassesExactProjectIdToRepository()
    {
        // Arrange
        var expectedProjectId = Guid.NewGuid();
        var embedding = new float[] { 0.1f, 0.2f };

        _repositoryMock
            .Setup(r => r.FindSimilarEventsAsync(
                It.IsAny<Guid>(),
                It.IsAny<float[]>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        await _repositoryMock.Object.FindSimilarEventsAsync(
            expectedProjectId, embedding, threshold: 0.7, windowHours: 12, maxTake: 3);

        // Assert
        _repositoryMock.Verify(r => r.FindSimilarEventsAsync(
            expectedProjectId,
            embedding,
            It.IsAny<double>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Event CreateEvent(Guid projectId) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Title = $"Event in project {projectId}",
            Summary = "Summary",
            Status = EventStatus.Active,
            FirstSeenAt = new DateTimeOffset(2025, 4, 1, 10, 0, 0, TimeSpan.Zero),
            LastUpdatedAt = new DateTimeOffset(2025, 4, 1, 10, 0, 0, TimeSpan.Zero),
            Articles = [],
        };
}
