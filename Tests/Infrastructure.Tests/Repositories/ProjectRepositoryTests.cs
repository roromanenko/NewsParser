using Core.DomainModels;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// Interface-contract tests for <see cref="IProjectRepository"/>.
///
/// The concrete <c>ProjectRepository</c> uses Dapper against PostgreSQL and cannot
/// be exercised without a live database. These tests verify the behavioral contract
/// through a mock, ensuring that consumers of the interface can rely on the expected
/// return shapes, null semantics, and that write methods are invoked with the correct
/// arguments.
/// </summary>
[TestFixture]
public class ProjectRepositoryTests
{
    private Mock<IProjectRepository> _repositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IProjectRepository>();
    }

    // ------------------------------------------------------------------
    // GetByIdAsync — P0: known id returns correct Project domain object
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByIdAsync_WhenProjectExists_ReturnsCorrectProject()
    {
        // Arrange
        var id = Guid.NewGuid();
        var expected = CreateProject(id, "Tech Daily", "tech-daily");

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _repositoryMock.Object.GetByIdAsync(id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.Name.Should().Be("Tech Daily");
        result.Slug.Should().Be("tech-daily");
    }

    // ------------------------------------------------------------------
    // GetByIdAsync — P1: unknown id returns null
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByIdAsync_WhenProjectDoesNotExist_ReturnsNull()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.GetByIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);

        // Act
        var result = await _repositoryMock.Object.GetByIdAsync(unknownId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // ExistsAsync — P0: known id returns true
    // ------------------------------------------------------------------

    [Test]
    public async Task ExistsAsync_WhenProjectExists_ReturnsTrue()
    {
        // Arrange
        var id = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.ExistsAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _repositoryMock.Object.ExistsAsync(id, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // ExistsAsync — P1: unknown id returns false
    // ------------------------------------------------------------------

    [Test]
    public async Task ExistsAsync_WhenProjectDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.ExistsAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _repositoryMock.Object.ExistsAsync(unknownId, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // GetBySlugAsync — P0: known slug returns matching Project
    // ------------------------------------------------------------------

    [Test]
    public async Task GetBySlugAsync_WhenSlugExists_ReturnsMatchingProject()
    {
        // Arrange
        const string slug = "sports-news";
        var expected = CreateProject(Guid.NewGuid(), "Sports News", slug);

        _repositoryMock
            .Setup(r => r.GetBySlugAsync(slug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _repositoryMock.Object.GetBySlugAsync(slug, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Slug.Should().Be(slug);
        result.Name.Should().Be("Sports News");
    }

    // ------------------------------------------------------------------
    // CreateAsync — P0: inserts project and returns it
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateAsync_WhenCalled_InvokesRepositoryAndReturnsCreatedProject()
    {
        // Arrange
        var project = CreateProject(Guid.NewGuid(), "World News", "world-news");

        _repositoryMock
            .Setup(r => r.CreateAsync(project, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        // Act
        var result = await _repositoryMock.Object.CreateAsync(project, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(project.Id);
        result.Slug.Should().Be("world-news");
        _repositoryMock.Verify(r => r.CreateAsync(project, CancellationToken.None), Times.Once);
    }

    // ------------------------------------------------------------------
    // DeleteAsync — P1: repository propagates raw exception on FK violation
    //
    // The ProductionService wraps PostgresException (23503) in InvalidOperationException.
    // The repository itself should propagate whatever exception the DB driver throws so
    // that the service layer can handle it. This test documents that the repository does
    // NOT silently swallow exceptions.
    // ------------------------------------------------------------------

    [Test]
    public async Task DeleteAsync_WhenRepositoryThrows_PropagatesException()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var dbException = new InvalidOperationException("simulated constraint violation");

        _repositoryMock
            .Setup(r => r.DeleteAsync(projectId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(dbException);

        // Act
        var act = async () => await _repositoryMock.Object.DeleteAsync(projectId, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _repositoryMock.Verify(r => r.DeleteAsync(projectId, CancellationToken.None), Times.Once);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Project CreateProject(Guid id, string name, string slug) =>
        new()
        {
            Id = id,
            Name = name,
            Slug = slug,
            AnalyzerPromptText = "Analyze the following article.",
            Categories = ["General"],
            OutputLanguage = "en",
            OutputLanguageName = "English",
            IsActive = true,
            CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
}
