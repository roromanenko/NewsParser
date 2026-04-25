using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Infrastructure.AI.Telemetry;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.AI.Telemetry;

/// <summary>
/// Tests for <see cref="AiRequestLogger"/>.
///
/// The logger builds an <see cref="AiRequestLog"/> row from an entry, computes
/// cost via the calculator, and writes the row through
/// <see cref="IAiRequestLogRepository"/>. It must swallow every persistence
/// failure (the "never break business flow" contract) except
/// <see cref="OperationCanceledException"/>, which must propagate for correct
/// host-shutdown behaviour. The logger is also responsible for truncating
/// <c>ErrorMessage</c> to 500 chars and setting <c>CostUsd</c> to the
/// calculator's return value on the persisted row.
/// </summary>
[TestFixture]
public class AiRequestLoggerTests
{
    private const string Provider = "Anthropic";
    private const string Model = "claude-haiku-4-5-20251001";
    private const string Operation = "Analyze";
    private const string Worker = "ArticleAnalysisWorker";

    private Mock<IAiCostCalculator> _calculatorMock = null!;
    private Mock<IAiRequestLogRepository> _repositoryMock = null!;
    private Mock<ILogger<AiRequestLogger>> _loggerMock = null!;
    private AiRequestLogger _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _calculatorMock = new Mock<IAiCostCalculator>();
        _repositoryMock = new Mock<IAiRequestLogRepository>();
        _loggerMock = new Mock<ILogger<AiRequestLogger>>();
        _loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        _calculatorMock
            .Setup(c => c.Calculate(It.IsAny<AiUsage>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(0m);

        _sut = new AiRequestLogger(_calculatorMock.Object, _repositoryMock.Object, _loggerMock.Object);
    }

    // ------------------------------------------------------------------
    // P0 — happy path: repository succeeds, LogAsync completes and
    //       the row is written exactly once
    // ------------------------------------------------------------------

    [Test]
    public async Task LogAsync_WhenRepositorySucceeds_CompletesAndWritesRowOnce()
    {
        // Arrange
        var entry = CreateEntry();

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<AiRequestLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () => await _sut.LogAsync(entry, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        _repositoryMock.Verify(
            r => r.AddAsync(It.IsAny<AiRequestLog>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P0 — repository throws generic exception → LogAsync does NOT
    //       rethrow, emits a LogWarning, and AddAsync was called once
    //       (so we know the failure happened during the persistence
    //       attempt, not before it)
    // ------------------------------------------------------------------

    [Test]
    public async Task LogAsync_WhenRepositoryThrowsGenericException_LogsWarningAndDoesNotRethrow()
    {
        // Arrange
        var entry = CreateEntry();
        var dbFailure = new InvalidOperationException("DB unreachable");

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<AiRequestLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(dbFailure);

        // Act
        var act = async () => await _sut.LogAsync(entry, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync(
            "a persistence failure must never break the caller's AI flow");

        _repositoryMock.Verify(
            r => r.AddAsync(It.IsAny<AiRequestLog>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                dbFailure,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P0 — repository throws OperationCanceledException → LogAsync
    //       rethrows so cancellation propagates to the caller
    // ------------------------------------------------------------------

    [Test]
    public async Task LogAsync_WhenRepositoryThrowsOperationCanceled_PropagatesCancellation()
    {
        // Arrange
        var entry = CreateEntry();

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<AiRequestLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var act = async () => await _sut.LogAsync(entry, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------------
    // P0 — CostUsd on the persisted row equals the calculator's return
    //       value (verifies that BuildLogRow uses the computed cost,
    //       not 0m or some default)
    // ------------------------------------------------------------------

    [Test]
    public async Task LogAsync_WhenCalculatorReturnsCost_PersistsThatExactCostUsdOnTheRow()
    {
        // Arrange
        const decimal expectedCost = 0.01234567m;
        var entry = CreateEntry();

        _calculatorMock
            .Setup(c => c.Calculate(entry.Usage, entry.Provider, entry.Model))
            .Returns(expectedCost);

        AiRequestLog? captured = null;
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<AiRequestLog>(), It.IsAny<CancellationToken>()))
            .Callback<AiRequestLog, CancellationToken>((row, _) => captured = row)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.LogAsync(entry, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.CostUsd.Should().Be(expectedCost);
    }

    // ------------------------------------------------------------------
    // P1 — an ErrorMessage longer than 500 chars is truncated to exactly
    //       500 chars on the persisted row (the SQL column is otherwise
    //       unbounded but long Claude stack traces bloat the table)
    // ------------------------------------------------------------------

    [Test]
    public async Task LogAsync_WhenErrorMessageExceedsFiveHundredChars_TruncatesToFiveHundred()
    {
        // Arrange
        var longMessage = new string('x', 800);
        var entry = CreateEntry() with { ErrorMessage = longMessage, Status = AiRequestStatus.Error };

        AiRequestLog? captured = null;
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<AiRequestLog>(), It.IsAny<CancellationToken>()))
            .Callback<AiRequestLog, CancellationToken>((row, _) => captured = row)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.LogAsync(entry, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.ErrorMessage.Should().NotBeNull();
        captured.ErrorMessage!.Length.Should().Be(500);
        captured.ErrorMessage.Should().Be(new string('x', 500));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static AiRequestLogEntry CreateEntry() => new(
        Provider: Provider,
        Operation: Operation,
        Model: Model,
        Usage: new AiUsage(InputTokens: 100, OutputTokens: 50, CacheCreationInputTokens: 0, CacheReadInputTokens: 0),
        LatencyMs: 250,
        Status: AiRequestStatus.Success,
        ErrorMessage: null,
        CorrelationId: Guid.NewGuid(),
        ArticleId: Guid.NewGuid(),
        Worker: Worker);
}
