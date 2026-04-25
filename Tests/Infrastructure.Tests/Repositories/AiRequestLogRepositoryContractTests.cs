using Core.DomainModels;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Infrastructure.Persistence.Connection;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Persistence.UnitOfWork;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// Tests for <see cref="IAiRequestLogRepository"/> interface contract.
///
/// The Dapper implementation (<see cref="AiRequestLogRepository"/>) uses a real
/// PostgreSQL connection for the INSERT, which is not available in-process.
/// Those aspects are verified against the <see cref="IAiRequestLogRepository"/>
/// interface mock, mirroring the pattern in
/// <c>PublicationRepositoryInterfaceContractTests.cs</c>.
///
/// The null-argument contract is verified directly against the concrete
/// repository because <c>ArgumentNullException.ThrowIfNull</c> runs before any
/// database interaction and can be exercised with mocked dependencies.
/// </summary>
[TestFixture]
public class AiRequestLogRepositoryContractTests
{
    private Mock<IAiRequestLogRepository> _repositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IAiRequestLogRepository>();
    }

    // ------------------------------------------------------------------
    // P0 — AddAsync with a valid log entry completes without throwing
    // ------------------------------------------------------------------

    [Test]
    public async Task AddAsync_WhenCalledWithValidEntry_CompletesWithoutThrowing()
    {
        // Arrange
        var entry = CreateValidLog();

        _repositoryMock
            .Setup(r => r.AddAsync(entry, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () => await _repositoryMock.Object.AddAsync(entry, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        _repositoryMock.Verify(
            r => r.AddAsync(entry, CancellationToken.None),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P0 — The cancellation token is forwarded to the repository call
    //       unchanged (critical for host-shutdown propagation)
    // ------------------------------------------------------------------

    [Test]
    public async Task AddAsync_WhenCalledWithCancellationToken_ForwardsThatExactTokenToImplementation()
    {
        // Arrange
        var entry = CreateValidLog();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        CancellationToken capturedToken = default;
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<AiRequestLog>(), It.IsAny<CancellationToken>()))
            .Callback<AiRequestLog, CancellationToken>((_, t) => capturedToken = t)
            .Returns(Task.CompletedTask);

        // Act
        await _repositoryMock.Object.AddAsync(entry, token);

        // Assert
        capturedToken.Should().Be(token);
        _repositoryMock.Verify(
            r => r.AddAsync(entry, token),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P1 — Two consecutive AddAsync calls record two distinct
    //       invocations (no accidental batching or deduplication)
    // ------------------------------------------------------------------

    [Test]
    public async Task AddAsync_WhenCalledTwice_RecordsTwoDistinctInvocations()
    {
        // Arrange
        var firstEntry = CreateValidLog();
        var secondEntry = CreateValidLog();

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<AiRequestLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _repositoryMock.Object.AddAsync(firstEntry, CancellationToken.None);
        await _repositoryMock.Object.AddAsync(secondEntry, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.AddAsync(firstEntry, It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(
            r => r.AddAsync(secondEntry, It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(
            r => r.AddAsync(It.IsAny<AiRequestLog>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ------------------------------------------------------------------
    // P1 — The concrete AiRequestLogRepository rejects a null entry with
    //       ArgumentNullException. Verified on the real class because
    //       the behaviour is implementation-level; mocked dependencies
    //       are never invoked because ThrowIfNull runs first.
    // ------------------------------------------------------------------

    [Test]
    public async Task AddAsync_WhenEntryIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var factoryMock = new Mock<IDbConnectionFactory>();
        var uowMock = new Mock<IUnitOfWork>();
        var sut = new AiRequestLogRepository(factoryMock.Object, uowMock.Object);

        // Act
        var act = async () => await sut.AddAsync(null!, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<ArgumentNullException>())
            .Which.ParamName.Should().Be("entry");

        // Mocked dependencies must never have been touched — the null guard
        // must run before any connection or transaction work.
        factoryMock.VerifyNoOtherCalls();
        uowMock.VerifyNoOtherCalls();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static AiRequestLog CreateValidLog() => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UtcNow,
        Worker = "ArticleAnalysisWorker",
        Provider = "Anthropic",
        Operation = "Analyze",
        Model = "claude-haiku-4-5-20251001",
        InputTokens = 100,
        OutputTokens = 50,
        CacheCreationInputTokens = 0,
        CacheReadInputTokens = 0,
        TotalTokens = 150,
        CostUsd = 0.00015m,
        LatencyMs = 350,
        Status = AiRequestStatus.Success,
        ErrorMessage = null,
        CorrelationId = Guid.NewGuid(),
        ArticleId = Guid.NewGuid()
    };
}
