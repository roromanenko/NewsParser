using Api.Controllers;
using Api.Models;
using Core.DomainModels;
using Core.Interfaces.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace Api.Tests.Controllers;

/// <summary>
/// Unit tests for <see cref="ProjectsController"/>.
///
/// The controller is instantiated directly (no WebApplicationFactory) so that
/// the tests are pure and fast. Auth and FluentValidation are exercised separately
/// via the integration tests in <see cref="ScopedControllerIsolationTests"/>.
/// Service-throws-InvalidOperationException → 409 mapping is documented here but
/// must be wired through <c>ExceptionMiddleware</c> in real HTTP pipelines; the unit
/// test verifies that the controller propagates the exception unmodified.
/// </summary>
[TestFixture]
public class ProjectsControllerTests
{
    private Mock<IProjectService> _projectServiceMock = null!;
    private ProjectsController _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _projectServiceMock = new Mock<IProjectService>();
        _sut = new ProjectsController(_projectServiceMock.Object);
    }

    // ------------------------------------------------------------------
    // GetAll — P0: returns 200 with list of ProjectListItemDto
    // ------------------------------------------------------------------

    [Test]
    public async Task GetAll_WhenProjectsExist_Returns200WithListOfProjectListItemDto()
    {
        // Arrange
        var projects = new List<Project>
        {
            CreateProject(Guid.NewGuid(), "News A", "news-a"),
            CreateProject(Guid.NewGuid(), "News B", "news-b"),
        };

        _projectServiceMock
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(projects);

        // Act
        var actionResult = await _sut.GetAll();

        // Assert
        var ok = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<List<ProjectListItemDto>>().Subject;
        dtos.Should().HaveCount(2);
        dtos[0].Name.Should().Be("News A");
        dtos[1].Name.Should().Be("News B");
    }

    // ------------------------------------------------------------------
    // GetById — P0: known id returns 200 with ProjectDetailDto
    // ------------------------------------------------------------------

    [Test]
    public async Task GetById_WhenProjectExists_Returns200WithProjectDetailDto()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var project = CreateProject(projectId, "World News", "world-news");

        _projectServiceMock
            .Setup(s => s.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        // Act
        var actionResult = await _sut.GetById(projectId);

        // Assert
        var ok = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ProjectDetailDto>().Subject;
        dto.Id.Should().Be(projectId);
        dto.Name.Should().Be("World News");
    }

    // ------------------------------------------------------------------
    // GetById — P1: unknown id returns 404
    // ------------------------------------------------------------------

    [Test]
    public async Task GetById_WhenProjectDoesNotExist_Returns404()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        _projectServiceMock
            .Setup(s => s.GetByIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);

        // Act
        var actionResult = await _sut.GetById(unknownId);

        // Assert
        actionResult.Result.Should().BeOfType<NotFoundResult>();
    }

    // ------------------------------------------------------------------
    // Create — P0: valid request returns 201 with ProjectDetailDto
    // ------------------------------------------------------------------

    [Test]
    public async Task Create_WhenRequestIsValid_Returns201WithProjectDetailDto()
    {
        // Arrange
        var request = new CreateProjectRequest(
            Name: "Health Daily",
            Slug: "health-daily",
            AnalyzerPromptText: "Analyze health news.",
            Categories: ["Health"],
            OutputLanguage: "en",
            OutputLanguageName: "English");

        var createdProject = CreateProject(Guid.NewGuid(), request.Name, request.Slug!);

        _projectServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdProject);

        // Act
        var actionResult = await _sut.Create(request);

        // Assert
        var created = actionResult.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.StatusCode.Should().Be(201);
        var dto = created.Value.Should().BeOfType<ProjectDetailDto>().Subject;
        dto.Name.Should().Be("Health Daily");
        _projectServiceMock.Verify(s => s.CreateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // Update — P0: valid request returns 200
    // ------------------------------------------------------------------

    [Test]
    public async Task Update_WhenProjectExists_Returns200WithUpdatedDto()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var existing = CreateProject(projectId, "Old Name", "old-slug");
        var request = new UpdateProjectRequest(
            Name: "New Name",
            AnalyzerPromptText: "New prompt.",
            Categories: ["Politics"],
            OutputLanguage: "uk",
            OutputLanguageName: "Ukrainian",
            IsActive: true);

        _projectServiceMock
            .Setup(s => s.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _projectServiceMock
            .Setup(s => s.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var actionResult = await _sut.Update(projectId, request);

        // Assert
        var ok = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ProjectDetailDto>().Subject;
        dto.Name.Should().Be("New Name");
        _projectServiceMock.Verify(s => s.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // UpdateStatus — P0: toggle active returns 204
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateStatus_WhenCalled_Returns204()
    {
        // Arrange
        var projectId = Guid.NewGuid();

        _projectServiceMock
            .Setup(s => s.UpdateActiveAsync(projectId, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.UpdateStatus(projectId, false);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _projectServiceMock.Verify(s => s.UpdateActiveAsync(projectId, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // Delete — P0: non-default id returns 204
    // ------------------------------------------------------------------

    [Test]
    public async Task Delete_WhenProjectExists_Returns204()
    {
        // Arrange
        var projectId = Guid.NewGuid();

        _projectServiceMock
            .Setup(s => s.DeleteAsync(projectId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.Delete(projectId);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _projectServiceMock.Verify(s => s.DeleteAsync(projectId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // Delete — P1: Default project causes service to throw InvalidOperationException
    //             (ExceptionMiddleware maps this to 409 in the real pipeline)
    // ------------------------------------------------------------------

    [Test]
    public async Task Delete_WhenServiceThrowsInvalidOperationException_PropagatesException()
    {
        // Arrange
        var defaultId = ProjectConstants.DefaultProjectId;

        _projectServiceMock
            .Setup(s => s.DeleteAsync(defaultId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The Default project cannot be deleted"));

        // Act
        var act = async () => await _sut.Delete(defaultId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Default project*");
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
            AnalyzerPromptText = "Analyze.",
            Categories = ["General"],
            OutputLanguage = "en",
            OutputLanguageName = "English",
            IsActive = true,
            CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
}
