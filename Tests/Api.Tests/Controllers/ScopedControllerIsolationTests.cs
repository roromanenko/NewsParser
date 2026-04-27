using Api.Controllers;
using Api.ProjectContext;
using Core.DomainModels;
using Core.Interfaces;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using FluentAssertions;
using Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Api.Tests.Controllers;

/// <summary>
/// Contract tests verifying that each scoped controller passes
/// <c>IProjectContext.ProjectId</c> — and not a hardcoded or empty Guid — to
/// its underlying service or repository when querying data.
///
/// These are pure unit tests: each controller is instantiated directly with mocked
/// dependencies. No live DB or HTTP pipeline is involved.
/// </summary>
[TestFixture]
public class ScopedControllerIsolationTests
{
    private Mock<IArticleRepository> _articleRepoMock = null!;
    private Mock<IEventRepository> _eventRepoMock = null!;
    private Mock<IEventService> _eventServiceMock = null!;
    private Mock<ISourceService> _sourceServiceMock = null!;
    private IOptions<CloudflareR2Options> _r2Options = null!;

    [SetUp]
    public void SetUp()
    {
        _articleRepoMock = new Mock<IArticleRepository>();
        _eventRepoMock = new Mock<IEventRepository>();
        _eventServiceMock = new Mock<IEventService>();
        _sourceServiceMock = new Mock<ISourceService>();

        _r2Options = Options.Create(new CloudflareR2Options
        {
            PublicBaseUrl = "https://cdn.test.example.com"
        });

        // Default setups to avoid null-reference in controller pagination logic
        _articleRepoMock
            .Setup(r => r.GetAnalysisDoneAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _articleRepoMock
            .Setup(r => r.CountAnalysisDoneAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _eventRepoMock
            .Setup(r => r.GetPagedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ImportanceTier?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _eventRepoMock
            .Setup(r => r.CountAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<ImportanceTier?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _sourceServiceMock
            .Setup(s => s.GetAllByProjectAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    // ------------------------------------------------------------------
    // ArticlesController — passes projectContext.ProjectId to GetAnalysisDoneAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task ArticlesController_GetAll_PassesProjectContextIdToRepository()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var context = BuildContext(projectId);
        var controller = new ArticlesController(_articleRepoMock.Object, _eventRepoMock.Object, context, _r2Options);

        // Act
        await controller.GetAnalysisDone();

        // Assert
        _articleRepoMock.Verify(r => r.GetAnalysisDoneAsync(
            projectId,
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // EventsController — passes projectContext.ProjectId to GetPagedAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task EventsController_GetAll_PassesProjectContextIdToRepository()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var context = BuildContext(projectId);
        var controller = new EventsController(_eventRepoMock.Object, _eventServiceMock.Object, context, _r2Options);

        // Act
        await controller.GetAll();

        // Assert
        _eventRepoMock.Verify(r => r.GetPagedAsync(
            projectId,
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<ImportanceTier?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // SourcesController — passes projectContext.ProjectId to GetAllByProjectAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task SourcesController_GetAll_PassesProjectContextIdToService()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var context = BuildContext(projectId);
        var controller = new SourcesController(_sourceServiceMock.Object, context);

        // Act
        await controller.GetAll();

        // Assert
        _sourceServiceMock.Verify(s => s.GetAllByProjectAsync(projectId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // Isolation contract: switching projectId in context changes the argument
    // passed to the underlying repository
    // ------------------------------------------------------------------

    [Test]
    public async Task ArticlesController_WhenProjectContextChanges_RepositoryReceivesDifferentProjectId()
    {
        // Arrange — first call
        var projectIdA = Guid.NewGuid();
        var contextA = BuildContext(projectIdA);
        var controllerA = new ArticlesController(_articleRepoMock.Object, _eventRepoMock.Object, contextA, _r2Options);

        // Act
        await controllerA.GetAnalysisDone();

        // Assert — first call used projectIdA
        _articleRepoMock.Verify(r => r.GetAnalysisDoneAsync(
            projectIdA,
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Arrange — second call with a different project context
        var projectIdB = Guid.NewGuid();
        var contextB = BuildContext(projectIdB);
        var controllerB = new ArticlesController(_articleRepoMock.Object, _eventRepoMock.Object, contextB, _r2Options);

        // Act
        await controllerB.GetAnalysisDone();

        // Assert — second call used projectIdB (not projectIdA)
        _articleRepoMock.Verify(r => r.GetAnalysisDoneAsync(
            projectIdB,
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Ensure projectIdA was only called once — not twice
        _articleRepoMock.Verify(r => r.GetAnalysisDoneAsync(
            projectIdA,
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static IProjectContext BuildContext(Guid projectId)
    {
        var context = new ProjectContextService();
        context.Set(new Project
        {
            Id = projectId,
            Name = "Test Project",
            Slug = "test-project",
            AnalyzerPromptText = "Analyze.",
            Categories = ["General"],
            OutputLanguage = "en",
            OutputLanguageName = "English",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return context;
    }
}
