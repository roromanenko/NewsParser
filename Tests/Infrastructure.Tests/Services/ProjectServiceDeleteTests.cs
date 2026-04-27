using Core.Interfaces.Repositories;
using FluentAssertions;
using Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using NUnit.Framework;

namespace Infrastructure.Tests.Services;

/// <summary>
/// Tests specifically for the FK-violation handling in <see cref="ProjectService.DeleteAsync"/>.
///
/// The service catches <see cref="PostgresException"/> with SqlState "23503" (foreign_key_violation)
/// and rethrows it as <see cref="InvalidOperationException"/> so that the API layer can map
/// it to HTTP 409 via <c>ExceptionMiddleware</c>.
/// </summary>
[TestFixture]
public class ProjectServiceDeleteTests
{
    private Mock<IProjectRepository> _repositoryMock = null!;
    private ProjectService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IProjectRepository>();
        var loggerMock = new Mock<ILogger<ProjectService>>();
        _sut = new ProjectService(_repositoryMock.Object, loggerMock.Object);
    }

    // ------------------------------------------------------------------
    // P1: FK violation (SqlState 23503) is wrapped in InvalidOperationException
    // ------------------------------------------------------------------

    [Test]
    public async Task DeleteAsync_WhenRepositoryThrowsFkViolation_ThrowsInvalidOperationException()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var postgresException = new PostgresException(
            "insert or update on table violates foreign key constraint",
            "ERROR",
            "ERROR",
            "23503");

        _repositoryMock
            .Setup(r => r.DeleteAsync(projectId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(postgresException);

        // Act
        var act = async () => await _sut.DeleteAsync(projectId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*children*");
    }

    // ------------------------------------------------------------------
    // P0: no exception from repository means DeleteAsync completes cleanly
    // ------------------------------------------------------------------

    [Test]
    public async Task DeleteAsync_WhenRepositorySucceeds_DoesNotThrow()
    {
        // Arrange
        var projectId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.DeleteAsync(projectId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () => await _sut.DeleteAsync(projectId);

        // Assert
        await act.Should().NotThrowAsync();
        _repositoryMock.Verify(r => r.DeleteAsync(projectId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
