using Core.DomainModels;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Services;

[TestFixture]
public class ProjectServiceTests
{
    private Mock<IProjectRepository> _repositoryMock = null!;
    private Mock<ILogger<ProjectService>> _loggerMock = null!;
    private ProjectService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IProjectRepository>();
        _loggerMock = new Mock<ILogger<ProjectService>>();
        _sut = new ProjectService(_repositoryMock.Object, _loggerMock.Object);
    }

    // ------------------------------------------------------------------
    // CreateAsync — P0: slug is derived from name when not provided
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateAsync_WhenSlugIsEmpty_DerivesSlugFromName()
    {
        // Arrange
        var project = CreateProject(name: "My News Feed", slug: string.Empty);

        _repositoryMock
            .Setup(r => r.GetBySlugAsync("my-news-feed", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);
        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project p, CancellationToken _) => p);

        // Act
        var result = await _sut.CreateAsync(project);

        // Assert
        result.Slug.Should().Be("my-news-feed");
        _repositoryMock.Verify(r => r.CreateAsync(
            It.Is<Project>(p => p.Slug == "my-news-feed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // CreateAsync — P1: slug collision throws InvalidOperationException
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateAsync_WhenSlugAlreadyExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var project = CreateProject(name: "Tech News", slug: "tech-news");
        var existingProject = CreateProject(name: "Existing Tech News", slug: "tech-news");

        _repositoryMock
            .Setup(r => r.GetBySlugAsync("tech-news", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingProject);

        // Act
        var act = async () => await _sut.CreateAsync(project);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Slug already in use");
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // CreateAsync — P0: unique slug calls repository CreateAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateAsync_WhenSlugIsUnique_CallsRepositoryCreate()
    {
        // Arrange
        var project = CreateProject(name: "Sports Daily", slug: "sports-daily");

        _repositoryMock
            .Setup(r => r.GetBySlugAsync("sports-daily", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);
        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project p, CancellationToken _) => p);

        // Act
        await _sut.CreateAsync(project);

        // Assert
        _repositoryMock.Verify(r => r.CreateAsync(
            It.Is<Project>(p => p.Slug == "sports-daily"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // DeleteAsync — P1: deleting the Default project throws InvalidOperationException
    // ------------------------------------------------------------------

    [Test]
    public async Task DeleteAsync_WhenIdIsDefaultProjectId_ThrowsInvalidOperationException()
    {
        // Arrange
        var defaultId = ProjectConstants.DefaultProjectId;

        // Act
        var act = async () => await _sut.DeleteAsync(defaultId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Default project*");
        _repositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // DeleteAsync — P0: non-default id delegates to repository
    // ------------------------------------------------------------------

    [Test]
    public async Task DeleteAsync_WhenIdIsNotDefaultProjectId_CallsRepositoryDelete()
    {
        // Arrange
        var projectId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.DeleteAsync(projectId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.DeleteAsync(projectId);

        // Assert
        _repositoryMock.Verify(r => r.DeleteAsync(projectId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // UpdateActiveAsync — P1: deactivating Default project calls repository AND logs warning
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateActiveAsync_WhenDeactivatingDefaultProject_CallsRepositoryAndLogsWarning()
    {
        // Arrange
        var defaultId = ProjectConstants.DefaultProjectId;

        _repositoryMock
            .Setup(r => r.UpdateActiveAsync(defaultId, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.UpdateActiveAsync(defaultId, false);

        // Assert
        _repositoryMock.Verify(r => r.UpdateActiveAsync(defaultId, false, It.IsAny<CancellationToken>()), Times.Once);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // UpdateActiveAsync — P0: non-default id calls repository without logging
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateActiveAsync_WhenIdIsNotDefaultProjectId_CallsRepositoryWithoutLoggingWarning()
    {
        // Arrange
        var projectId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.UpdateActiveAsync(projectId, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.UpdateActiveAsync(projectId, false);

        // Assert
        _repositoryMock.Verify(r => r.UpdateActiveAsync(projectId, false, It.IsAny<CancellationToken>()), Times.Once);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Project CreateProject(string name = "Test Project", string slug = "test-project") =>
        new()
        {
            Name = name,
            Slug = slug,
            AnalyzerPromptText = "Analyze this article.",
            Categories = ["Technology"],
            OutputLanguage = "en",
            OutputLanguageName = "English",
            IsActive = true,
        };
}
