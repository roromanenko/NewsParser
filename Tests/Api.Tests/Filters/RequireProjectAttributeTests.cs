using Api.Filters;
using Api.ProjectContext;
using Core.DomainModels;
using Core.Interfaces;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace Api.Tests.Filters;

[TestFixture]
public class RequireProjectAttributeTests
{
    private Mock<IProjectRepository> _projectRepositoryMock = null!;
    private ProjectContextService _projectContext = null!;
    private RequireProjectAttribute _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _projectRepositoryMock = new Mock<IProjectRepository>();
        _projectContext = new ProjectContextService();
        _sut = new RequireProjectAttribute();
    }

    // ------------------------------------------------------------------
    // P1: project not found → short-circuit with 404
    // ------------------------------------------------------------------

    [Test]
    public async Task OnActionExecutionAsync_WhenProjectNotFound_ReturnsNotFoundResult()
    {
        // Arrange
        var projectId = Guid.NewGuid();

        _projectRepositoryMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);

        var context = BuildActionExecutingContext(projectId, "GET");

        bool nextCalled = false;
        Task<ActionExecutedContext> Next()
        {
            nextCalled = true;
            return Task.FromResult(BuildActionExecutedContext(context));
        }

        // Act
        await _sut.OnActionExecutionAsync(context, Next);

        // Assert
        context.Result.Should().BeOfType<NotFoundObjectResult>();
        nextCalled.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // P0: project found and active, GET request → sets context and calls next
    // ------------------------------------------------------------------

    [Test]
    public async Task OnActionExecutionAsync_WhenProjectFoundAndActiveOnGet_SetsContextAndCallsNext()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var project = CreateActiveProject(projectId);

        _projectRepositoryMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        var context = BuildActionExecutingContext(projectId, "GET");

        bool nextCalled = false;
        Task<ActionExecutedContext> Next()
        {
            nextCalled = true;
            return Task.FromResult(BuildActionExecutedContext(context));
        }

        // Act
        await _sut.OnActionExecutionAsync(context, Next);

        // Assert
        context.Result.Should().BeNull();
        nextCalled.Should().BeTrue();
        _projectContext.IsSet.Should().BeTrue();
        _projectContext.ProjectId.Should().Be(projectId);
    }

    // ------------------------------------------------------------------
    // P1: project found, IsActive=false, GET request → reads are allowed, calls next
    // ------------------------------------------------------------------

    [Test]
    public async Task OnActionExecutionAsync_WhenProjectInactiveOnGet_CallsNext()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var project = CreateInactiveProject(projectId);

        _projectRepositoryMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        var context = BuildActionExecutingContext(projectId, "GET");

        bool nextCalled = false;
        Task<ActionExecutedContext> Next()
        {
            nextCalled = true;
            return Task.FromResult(BuildActionExecutedContext(context));
        }

        // Act
        await _sut.OnActionExecutionAsync(context, Next);

        // Assert
        context.Result.Should().BeNull();
        nextCalled.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // P1: project found, IsActive=false, POST request → short-circuit with 409
    // ------------------------------------------------------------------

    [Test]
    public async Task OnActionExecutionAsync_WhenProjectInactiveOnPost_ReturnsConflictResult()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var project = CreateInactiveProject(projectId);

        _projectRepositoryMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        var context = BuildActionExecutingContext(projectId, "POST");

        bool nextCalled = false;
        Task<ActionExecutedContext> Next()
        {
            nextCalled = true;
            return Task.FromResult(BuildActionExecutedContext(context));
        }

        // Act
        await _sut.OnActionExecutionAsync(context, Next);

        // Assert
        context.Result.Should().BeOfType<ConflictObjectResult>();
        nextCalled.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // P0: project found and active, POST request → sets context and calls next
    // ------------------------------------------------------------------

    [Test]
    public async Task OnActionExecutionAsync_WhenProjectFoundAndActiveOnPost_SetsContextAndCallsNext()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var project = CreateActiveProject(projectId);

        _projectRepositoryMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        var context = BuildActionExecutingContext(projectId, "POST");

        bool nextCalled = false;
        Task<ActionExecutedContext> Next()
        {
            nextCalled = true;
            return Task.FromResult(BuildActionExecutedContext(context));
        }

        // Act
        await _sut.OnActionExecutionAsync(context, Next);

        // Assert
        context.Result.Should().BeNull();
        nextCalled.Should().BeTrue();
        _projectContext.IsSet.Should().BeTrue();
        _projectContext.ProjectId.Should().Be(projectId);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private ActionExecutingContext BuildActionExecutingContext(Guid projectId, string httpMethod)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IProjectRepository>(_projectRepositoryMock.Object);
        serviceCollection.AddSingleton<IProjectContext>(_projectContext);
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        httpContext.Request.Method = httpMethod;

        var routeData = new RouteData();
        routeData.Values["projectId"] = projectId.ToString();

        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        return new ActionExecutingContext(
            actionContext,
            filters: [],
            actionArguments: new Dictionary<string, object?>(),
            controller: new object());
    }

    private static ActionExecutedContext BuildActionExecutedContext(ActionExecutingContext context) =>
        new(context, filters: [], controller: new object());

    private static Project CreateActiveProject(Guid id) =>
        new()
        {
            Id = id,
            Name = "Active Project",
            Slug = "active-project",
            AnalyzerPromptText = "Analyze.",
            Categories = ["General"],
            OutputLanguage = "en",
            OutputLanguageName = "English",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static Project CreateInactiveProject(Guid id)
    {
        var project = CreateActiveProject(id);
        project.IsActive = false;
        return project;
    }
}
