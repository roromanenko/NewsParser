using Api.Controllers;
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
/// Contract tests verifying that each scoped controller reads <c>IProjectContext.ProjectId</c>
/// and forwards it verbatim to the underlying service/repository call.
///
/// Unlike <see cref="ScopedControllerIsolationTests"/> (which uses a real
/// <c>ProjectContextService</c>), these tests inject <see cref="IProjectContext"/> as a
/// <c>Mock</c> to assert the exact projectId forwarded.
/// </summary>
[TestFixture]
public class ScopedControllerProjectContextTests
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
    // ArticlesController — forwards IProjectContext.ProjectId to GetAnalysisDoneAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task ArticlesController_GetAll_PassesProjectIdToService()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var contextMock = new Mock<IProjectContext>();
        contextMock.Setup(c => c.ProjectId).Returns(projectId);

        var controller = new ArticlesController(
            _articleRepoMock.Object,
            _eventRepoMock.Object,
            contextMock.Object,
            _r2Options);

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
    // EventsController — forwards IProjectContext.ProjectId to GetPagedAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task EventsController_GetAll_PassesProjectIdToService()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var contextMock = new Mock<IProjectContext>();
        contextMock.Setup(c => c.ProjectId).Returns(projectId);

        var controller = new EventsController(
            _eventRepoMock.Object,
            _eventServiceMock.Object,
            contextMock.Object,
            _r2Options);

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
    // SourcesController — forwards IProjectContext.ProjectId to GetAllByProjectAsync
    // ------------------------------------------------------------------

    [Test]
    public async Task SourcesController_GetAll_PassesProjectIdToService()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var contextMock = new Mock<IProjectContext>();
        contextMock.Setup(c => c.ProjectId).Returns(projectId);

        var controller = new SourcesController(_sourceServiceMock.Object, contextMock.Object);

        // Act
        await controller.GetAll();

        // Assert
        _sourceServiceMock.Verify(s => s.GetAllByProjectAsync(
            projectId,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
